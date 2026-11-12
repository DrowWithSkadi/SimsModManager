# Sims Mod Manager — мод-помощник.
#
# Связывает игру с программой Sims Mod Manager через папку bridge рядом с этим файлом:
#   status.json        — игра пишет раз в секунду: жива ли, где сейчас (меню / в игре), какое сохранение, пауза
#   cmd-<id>.json      — программа кладёт команду
#   res-<id>.json      — игра отвечает на команду
# Никаких изменений в игре помощник сам по себе не делает — только выполняет команды программы.
#
# Команды можно проверить и вручную в консоли игры (Ctrl+Shift+C): smm.status, smm.save, smm.pause, smm.resume.

import json
import os
import threading
import time
import traceback

import alarms
import clock
import services
import sims4.commands
from clock import ClockSpeedMode, GameSpeedChangeSource
from sims4.commands import CommandType

VERSION = 2
PAUSE_REASON = 'SimsModManager'
POLL_SECONDS = 0.25


def _find_bridge_folder():
    """Папка bridge рядом с .ts4script (Mods/SimsModManager/bridge)."""
    path = os.path.abspath(__file__)
    while True:
        parent = os.path.dirname(path)
        if parent == path:
            return None
        if path.lower().endswith('.ts4script'):
            return os.path.join(parent, 'bridge')
        path = parent


BRIDGE = _find_bridge_folder()

_state = {
    'state': 'starting',   # starting / menu / loading / live
    'slot_id': None,
    'slot_name': None,
    'paused_by_us': False,
    'swap': None,          # подмена на лету: None / pending / applied / failed
    'swap_error': None,
}
_pending_swap = None
_lock = threading.Lock()
_alarm = None
_owner = None


# ---------- файлы ----------

def _write_json(name, data):
    if BRIDGE is None:
        return
    os.makedirs(BRIDGE, exist_ok=True)
    target = os.path.join(BRIDGE, name)
    temp = target + '.tmp'
    with open(temp, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False)
    os.replace(temp, target)


def _log(text):
    try:
        if BRIDGE is None:
            return
        os.makedirs(BRIDGE, exist_ok=True)
        with open(os.path.join(BRIDGE, 'helper.log'), 'a', encoding='utf-8') as f:
            f.write(time.strftime('%Y-%m-%d %H:%M:%S ') + text + '\n')
    except Exception:
        pass


def _write_status():
    with _lock:
        data = dict(_state)
    data['version'] = VERSION
    data['pid'] = os.getpid()
    data['time'] = time.time()
    _write_json('status.json', data)


def _heartbeat():
    # Отдельный поток только пишет файл — с игрой он не взаимодействует, поэтому безопасен
    while True:
        try:
            _write_status()
        except Exception:
            pass
        time.sleep(1.0)


# ---------- игра ----------

def _client():
    try:
        return services.client_manager().get_first_client()
    except Exception:
        return None


def _refresh_slot():
    try:
        slot = services.get_persistence_service().get_save_slot_proto_buff()
        if slot is not None:
            with _lock:
                _state['slot_id'] = int(slot.slot_id)
                _state['slot_name'] = str(slot.slot_name)
    except Exception:
        pass


def _is_paused():
    try:
        return services.game_clock_service().clock_speed == ClockSpeedMode.PAUSED
    except Exception:
        return False


def _pause():
    with _lock:
        if _state['paused_by_us']:
            return
        _state['paused_by_us'] = True
    services.game_clock_service().push_speed(ClockSpeedMode.PAUSED, source=GameSpeedChangeSource.GAMEPLAY,
                                             reason=PAUSE_REASON, immediate=True)


def _resume():
    with _lock:
        if not _state['paused_by_us']:
            return
        _state['paused_by_us'] = False
    services.game_clock_service().pop_speed(ClockSpeedMode.PAUSED, source=GameSpeedChangeSource.GAMEPLAY,
                                            reason=PAUSE_REASON, immediate=True)


def _save(on_done):
    """Сохраняет игру в её текущий слот. on_done(ok, details) вызывается, когда файл записан."""
    from services.persistence_service import SaveGameData
    _refresh_slot()
    with _lock:
        slot_id = _state['slot_id']
        slot_name = _state['slot_name'] or 'Sims'
    if slot_id is None:
        on_done(False, 'Не удалось узнать текущее сохранение')
        return
    persistence = services.get_persistence_service()
    data = SaveGameData(slot_id, slot_name, True, None)

    def save_gen(timeline):
        result = yield from persistence.save_game_gen(timeline, data, send_save_message=True, check_cooldown=False)
        ok = int(result) == 0  # SaveGameResult.SUCCESS
        on_done(ok, {'slot_id': slot_id, 'slot_name': slot_name, 'result': int(result)})
        return result

    persistence.save_using(save_gen)


# ---------- подмена сохранения на лету (эксперимент) ----------
#
# Игра держит всё сохранение в памяти (persistence_service._save_game_data_proto) и при каждой
# загрузке участка заново строит мир из этих данных. Мы подкладываем туда данные точки прямо
# перед загрузкой участка и запускаем «переезд» — игра загружает участок уже из точки.

def _hotswap(path):
    global _pending_swap
    from protocolbuffers import FileSerialization_pb2 as serialization
    import distributor.ops

    with open(path, 'rb') as f:
        data = f.read()
    proto = serialization.SaveGameData()
    proto.ParseFromString(data)

    persistence = services.get_persistence_service()
    current = persistence._save_game_data_proto
    try:
        if current.guid and proto.guid and current.guid != proto.guid:
            raise Exception('Точка сделана в другом сохранении')
    except AttributeError:
        pass

    household_id = proto.save_slot.active_household_id
    candidates = [s for s in proto.sims if s.household_id == household_id and s.zone_id]
    if not candidates:
        raise Exception('В точке нет активной семьи')
    active = services.active_sim_info()
    target = next((s for s in candidates if active is not None and s.sim_id == active.id), candidates[0])
    zone_id = target.zone_id
    world_id = next((z.world_id for z in proto.zones if z.zone_id == zone_id), 0)

    _pending_swap = proto
    with _lock:
        _state['swap'] = 'pending'
        _state['swap_error'] = None
    _log('Подмена: точка прочитана (%d байт), переезд на участок %s' % (len(data), zone_id))

    owner = active if active is not None else services.sim_info_manager().get(target.sim_id)
    op = distributor.ops.TravelSwitchToZone((target.sim_id, household_id, zone_id, world_id))
    distributor.ops.record(owner, op)


def _cancel_swap():
    global _pending_swap
    _pending_swap = None
    with _lock:
        _state['swap'] = None


def _apply_pending_swap(persistence):
    global _pending_swap
    if _pending_swap is None:
        return
    proto, _pending_swap = _pending_swap, None
    try:
        persistence._save_game_data_proto.CopyFrom(proto)
        with _lock:
            _state['swap'] = 'applied'
        _log('Подмена: данные точки подставлены перед загрузкой участка')
    except Exception as e:
        with _lock:
            _state['swap'] = 'failed'
            _state['swap_error'] = str(e)
        _log('Подмена не удалась: %s\n%s' % (e, traceback.format_exc()))


def _run_cheat(line):
    client = _client()
    sims4.commands.execute(line, client.id if client is not None else None)


# ---------- команды от программы ----------

def _respond(command_id, ok, result=None, error=None):
    _write_json('res-%s.json' % command_id, {'id': command_id, 'ok': ok, 'result': result, 'error': error,
                                             'time': time.time()})


def _handle(command):
    command_id = command.get('id')
    name = command.get('command')
    if name == 'ping':
        _refresh_slot()
        _respond(command_id, True, {'paused': _is_paused(), 'slot_id': _state['slot_id'],
                                    'slot_name': _state['slot_name']})
    elif name == 'pause':
        _pause()
        _respond(command_id, True)
    elif name == 'resume':
        _resume()
        _respond(command_id, True)
    elif name == 'save':
        _save(lambda ok, details: _respond(command_id, ok, details if ok else None, None if ok else str(details)))
    elif name == 'hotswap':
        _hotswap(command.get('path', ''))
        _respond(command_id, True)
    elif name == 'hotswap_cancel':
        _cancel_swap()
        _respond(command_id, True)
    elif name == 'cheat':
        _run_cheat(command.get('line', ''))
        _respond(command_id, True)
    else:
        _respond(command_id, False, error='Неизвестная команда: %s' % name)


def _poll(_handle_alarm=None):
    if BRIDGE is None or not os.path.isdir(BRIDGE):
        return
    try:
        names = sorted(n for n in os.listdir(BRIDGE) if n.startswith('cmd-') and n.endswith('.json'))
    except Exception:
        return
    for name in names:
        path = os.path.join(BRIDGE, name)
        try:
            with open(path, 'r', encoding='utf-8') as f:
                command = json.load(f)
            os.remove(path)
        except Exception:
            continue  # файл ещё дописывается — заберём в следующий раз
        try:
            # Команда, которую программа уже перестала ждать, не выполняется
            if time.time() - float(command.get('time', 0)) > float(command.get('ttl', 10)):
                continue
            _handle(command)
        except Exception as e:
            _log('Ошибка команды %s: %s\n%s' % (command.get('command'), e, traceback.format_exc()))
            try:
                _respond(command.get('id'), False, error=str(e))
            except Exception:
                pass


class _Owner:
    pass


def _start_polling():
    global _alarm, _owner
    if _alarm is not None:
        try:
            alarms.cancel_alarm(_alarm)
        except Exception:
            pass
    _owner = _Owner()
    _alarm = alarms.add_alarm_real_time(_owner, clock.interval_in_real_seconds(POLL_SECONDS), _poll, repeating=True,
                                        use_sleep_time=False)


def _on_zone_ready():
    with _lock:
        _state['state'] = 'live'
        _state['paused_by_us'] = False
    _refresh_slot()
    try:
        _start_polling()
    except Exception as e:
        _log('Не удалось запустить опрос: %s' % e)


def _on_main_menu():
    global _alarm
    _alarm = None
    with _lock:
        _state['state'] = 'menu'
        _state['paused_by_us'] = False


# ---------- подключение к игре ----------

def _wrap(owner, name, after):
    original = getattr(owner, name)

    def wrapped(*args, **kwargs):
        result = original(*args, **kwargs)
        try:
            after()
        except Exception as e:
            _log('Ошибка в %s: %s' % (name, e))
        return result

    wrapped.__name__ = getattr(original, '__name__', name)
    setattr(owner, name, wrapped)


def _install():
    try:
        import zone
        _wrap(zone.Zone, 'on_loading_screen_animation_finished', _on_zone_ready)
    except Exception as e:
        _log('Не удалось подключиться к загрузке участка: %s' % e)
    try:
        from services.persistence_service import PersistenceService
        original_build = PersistenceService.build_caches

        def build_caches(self, *args, **kwargs):
            # Участок начинает загружаться — до конца загрузки команды не выполняются
            with _lock:
                _state['state'] = 'loading'
            try:
                _apply_pending_swap(self)
            except Exception as e:
                _log('Ошибка подмены: %s' % e)
            return original_build(self, *args, **kwargs)

        PersistenceService.build_caches = build_caches
    except Exception as e:
        _log('Не удалось подключиться к загрузке сохранения: %s' % e)
    try:
        _wrap(services, 'on_enter_main_menu', _on_main_menu)
    except Exception as e:
        _log('Не удалось подключиться к главному меню: %s' % e)
    try:
        if BRIDGE is not None:
            os.makedirs(BRIDGE, exist_ok=True)
            for n in os.listdir(BRIDGE):
                if n.startswith('cmd-') or n.startswith('res-'):
                    os.remove(os.path.join(BRIDGE, n))
        thread = threading.Thread(target=_heartbeat, name='SimsModManagerHeartbeat')
        thread.daemon = True
        thread.start()
    except Exception as e:
        _log('Не удалось запустить сигнал «жив»: %s' % e)
    _log('Помощник %s загружен, папка связи: %s' % (VERSION, BRIDGE))


_install()


# ---------- ручная проверка из консоли ----------

@sims4.commands.Command('smm.status', command_type=CommandType.Live)
def _cmd_status(_connection=None):
    out = sims4.commands.CheatOutput(_connection)
    _refresh_slot()
    out('Sims Mod Manager: помощник %s, состояние %s, сохранение %s (%s), пауза %s, связь %s' % (
        VERSION, _state['state'], _state['slot_name'], _state['slot_id'], _is_paused(), BRIDGE))


@sims4.commands.Command('smm.start', command_type=CommandType.Live)
def _cmd_start(_connection=None):
    _on_zone_ready()
    sims4.commands.CheatOutput(_connection)('Sims Mod Manager: связь запущена')


@sims4.commands.Command('smm.save', command_type=CommandType.Live)
def _cmd_save(_connection=None):
    out = sims4.commands.CheatOutput(_connection)
    _save(lambda ok, details: out('Sims Mod Manager: сохранение %s — %s' % ('готово' if ok else 'не удалось', details)))


@sims4.commands.Command('smm.pause', command_type=CommandType.Live)
def _cmd_pause(_connection=None):
    _pause()


@sims4.commands.Command('smm.resume', command_type=CommandType.Live)
def _cmd_resume(_connection=None):
    _resume()
