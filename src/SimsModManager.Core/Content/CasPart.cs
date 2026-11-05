using System.Text;

namespace SimsModManager.Core.Content;

/// <summary>Возраст и пол, для которых доступна вещь (битовые флаги из файла игры).</summary>
[Flags]
public enum AgeGender : uint
{
    None = 0,
    Baby = 0x0001,
    Toddler = 0x0002,
    Child = 0x0004,
    Teen = 0x0008,
    YoungAdult = 0x0010,
    Adult = 0x0020,
    Elder = 0x0040,
    Infant = 0x0080,
    Male = 0x1000,
    Female = 0x2000,
}

/// <summary>Дополнительные флаги (версия CASP 39+).</summary>
[Flags]
public enum CasParamFlags2 : byte
{
    None = 0,
    RestrictOppositeFrame = 1,
    DefaultForBodyTypeMale = 1 << 1,
    DefaultForBodyTypeFemale = 1 << 2,
}

/// <summary>Флаги «где и как можно использовать вещь».</summary>
[Flags]
public enum CasParamFlags : byte
{
    None = 0,
    DefaultThumbnailPart = 1 << 1,
    AllowForCasRandom = 1 << 2,
    ShowInUi = 1 << 3,
    ShowInSimInfoPanel = 1 << 4,
    ShowInCasDemo = 1 << 5,
    AllowForLiveRandom = 1 << 6,
    RestrictOppositeGender = 1 << 7,
}

public readonly record struct CasTag(ushort Category, uint Value);

/// <summary>
/// Начало ресурса CAS Part (CASP) — описание одной вещи для редактора персонажа.
/// Читаем только поля до «возраста/пола», этого хватает для списка и настроек.
/// Порядок полей сверен с открытыми инструментами TS4SimRipper и s4pe.
/// </summary>
public sealed class CasPart
{
    public const uint ResourceType = 0x034AEECB;

    public uint Version { get; private init; }
    public string Name { get; private init; } = "";
    public CasParamFlags Flags { get; private init; }
    public CasParamFlags2 Flags2 { get; private init; }
    public IReadOnlyList<CasTag> Tags { get; private init; } = [];
    public uint BodyType { get; private init; }
    public uint BodySubType { get; private init; }
    public AgeGender AgeGender { get; private init; }
    public uint Species { get; private init; } = 1;

    // Позиции полей в байтах — понадобятся, чтобы менять настройки вещи.
    public int FlagsOffset { get; private init; }
    public int TagsOffset { get; private init; }
    public int TagsEndOffset { get; private init; }
    public int AgeGenderOffset { get; private init; }

    public static CasPart Parse(byte[] data)
    {
        using var ms = new MemoryStream(data, writable: false);
        var r = new BinaryReader(ms);

        var version = r.ReadUInt32();
        r.ReadUInt32(); // смещение списка ссылок
        r.ReadInt32();  // число пресетов
        var name = ReadBigEndianString(r);
        r.ReadSingle(); // порядок сортировки
        r.ReadUInt16(); // порядок расцветки
        r.ReadUInt32(); // id наряда
        r.ReadUInt32(); // материал (звук шагов)

        var flagsOffset = (int)ms.Position;
        var flags = (CasParamFlags)r.ReadByte();
        var flags2 = version >= 39 ? (CasParamFlags2)r.ReadByte() : CasParamFlags2.None;
        if (version >= 50) r.ReadUInt16();

        if (version >= 51)
        {
            var n = r.ReadInt32();
            if (n < 0 || n > 64) throw new InvalidDataException("Повреждённый CASP.");
            ms.Position += n * 8L;
        }
        else
        {
            r.ReadUInt64();
            if (version >= 41) r.ReadUInt64();
        }
        if (version > 36) r.ReadUInt64(); else r.ReadUInt32();

        var tagsOffset = (int)ms.Position;
        var tagCount = r.ReadInt32();
        if (tagCount < 0 || tagCount > 10_000) throw new InvalidDataException("Повреждённый CASP.");
        var tags = new List<CasTag>(tagCount);
        for (var i = 0; i < tagCount; i++)
        {
            var cat = r.ReadUInt16();
            var val = version >= 37 ? r.ReadUInt32() : r.ReadUInt16();
            tags.Add(new CasTag(cat, val));
        }
        var tagsEnd = (int)ms.Position;

        r.ReadUInt32(); // цена
        r.ReadUInt32(); // ключ названия
        r.ReadUInt32(); // ключ описания
        if (version >= 0x2B) r.ReadUInt32();
        r.ReadByte();   // текстурное пространство
        var bodyType = r.ReadUInt32();
        var bodySubType = r.ReadUInt32();
        var ageGenderOffset = (int)ms.Position;
        var ageGender = (AgeGender)r.ReadUInt32();
        var species = version >= 32 ? r.ReadUInt32() : 1u;

        return new CasPart
        {
            Version = version,
            Name = name,
            Flags = flags,
            Flags2 = flags2,
            Tags = tags,
            BodyType = bodyType,
            BodySubType = bodySubType,
            AgeGender = ageGender,
            Species = species,
            FlagsOffset = flagsOffset,
            TagsOffset = tagsOffset,
            TagsEndOffset = tagsEnd,
            AgeGenderOffset = ageGenderOffset,
        };
    }

    /// <summary>Строка с длиной в формате 7-битного числа и текстом в UTF-16 Big Endian.</summary>
    private static string ReadBigEndianString(BinaryReader r)
    {
        int length = 0, shift = 0;
        byte b;
        do
        {
            b = r.ReadByte();
            length |= (b & 0x7F) << shift;
            shift += 7;
        } while ((b & 0x80) != 0 && shift < 35);
        if (length < 0 || length > 4096) throw new InvalidDataException("Повреждённый CASP.");
        return Encoding.BigEndianUnicode.GetString(r.ReadBytes(length));
    }

    // ---- Типы частей тела (BodyType) ----

    public const uint BodyHat = 1, BodyHair = 2, BodyHead = 3, BodyFace = 4, BodyFull = 5, BodyTop = 6,
        BodyBottom = 7, BodyShoes = 8, BodyFacialHair = 0x1C, BodySocks = 0x24, BodyTights = 0x2A;

    public ModCategory Category => BodyType switch
    {
        BodyHair or BodyFacialHair => ModCategory.Hair,
        BodyHat or BodyFull or BodyTop or BodyBottom or BodyShoes or BodySocks or BodyTights => ModCategory.Clothing,
        >= 0x09 and <= 0x1B => ModCategory.Clothing, // аксессуары, украшения, перчатки
        >= 0x1D and <= 0x3A => ModCategory.Makeup,   // макияж, глаза, брови, тату, детали кожи
        >= 0x3F and <= 0x50 => ModCategory.Makeup,   // детали кожи и ногти
        _ => ModCategory.Other,
    };

    public string BodyTypeName => BodyType switch
    {
        BodyHat => "Головной убор",
        BodyHair => "Причёска",
        BodyFull => "Цельный наряд",
        BodyTop => "Верх",
        BodyBottom => "Низ",
        BodyShoes => "Обувь",
        BodyFacialHair => "Борода",
        BodySocks => "Носки",
        BodyTights => "Колготки",
        >= 0x09 and <= 0x1B => "Аксессуар",
        0x1D => "Помада",
        0x1E => "Тени",
        0x1F => "Подводка",
        0x20 => "Румяна",
        0x21 => "Грим",
        0x22 => "Брови",
        0x23 => "Цвет глаз",
        0x25 => "Тушь",
        >= 0x2D and <= 0x36 => "Тату",
        _ => "Часть тела",
    };
}
