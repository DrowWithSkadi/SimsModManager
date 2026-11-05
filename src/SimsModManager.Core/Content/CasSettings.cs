using System.Buffers.Binary;

namespace SimsModManager.Core.Content;

/// <summary>Категории одежды в игре («Повседневная», «Парадная»…) — это метки CAS с категорией 70.</summary>
public static class OutfitTags
{
    public const ushort TagCategory = 70;

    public const uint Everyday = 77;
    public const uint Formal = 78;
    public const uint Swimwear = 79;
    public const uint Athletic = 80;
    public const uint Sleep = 81;
    public const uint Bathing = 82;
    public const uint Party = 83;
    public const uint Career = 263;
    public const uint Situation = 335;

    /// <summary>Метки, которыми управляет менеджер. Остальные метки этой категории (например, погода) не трогаем.</summary>
    public static readonly IReadOnlyList<(uint Value, string Name)> Known =
    [
        (Everyday, "Повседневная"),
        (Formal, "Парадная"),
        (Athletic, "Спортивная"),
        (Sleep, "Для сна"),
        (Party, "Вечеринка"),
        (Swimwear, "Купальная"),
        (Bathing, "Для купания"),
        (Career, "Работа"),
        (Situation, "Ситуация"),
    ];

    public static bool IsKnown(uint value) => Known.Any(k => k.Value == value);
}

/// <summary>
/// Настройки одной вещи, которые можно менять в менеджере: для кого, возраст,
/// категории одежды и можно ли игре надевать её на случайных симов.
/// </summary>
public sealed record CasSettings(
    AgeGender Ages,
    bool Female,
    bool Male,
    IReadOnlySet<uint> Outfits,
    bool AllowRandom)
{
    public const AgeGender AgeMask = AgeGender.Baby | AgeGender.Infant | AgeGender.Toddler | AgeGender.Child
                                     | AgeGender.Teen | AgeGender.YoungAdult | AgeGender.Adult | AgeGender.Elder;

    public static CasSettings From(CasPart part) => new(
        part.AgeGender & AgeMask,
        part.AgeGender.HasFlag(AgeGender.Female),
        part.AgeGender.HasFlag(AgeGender.Male),
        part.Tags.Where(t => t.Category == OutfitTags.TagCategory && OutfitTags.IsKnown(t.Value))
            .Select(t => t.Value).ToHashSet(),
        part.Flags.HasFlag(CasParamFlags.AllowForCasRandom) || part.Flags.HasFlag(CasParamFlags.AllowForLiveRandom));

    public bool SameAs(CasSettings other) =>
        Ages == other.Ages && Female == other.Female && Male == other.Male && AllowRandom == other.AllowRandom
        && Outfits.SetEquals(other.Outfits);
}

/// <summary>Запись новых настроек в байты ресурса CASP.</summary>
public static class CasPartWriter
{
    public static byte[] Apply(byte[] data, CasSettings settings)
    {
        var part = CasPart.Parse(data);
        var buffer = (byte[])data.Clone();

        // Возраст и пол (поле фиксированного размера — меняем на месте)
        var ag = part.AgeGender & ~(CasSettings.AgeMask | AgeGender.Male | AgeGender.Female);
        ag |= settings.Ages & CasSettings.AgeMask;
        if (settings.Female) ag |= AgeGender.Female;
        if (settings.Male) ag |= AgeGender.Male;
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(part.AgeGenderOffset), (uint)ag);

        // Флаги: случайные симы и запрет для другого пола
        var flags = part.Flags;
        flags = settings.AllowRandom
            ? flags | CasParamFlags.AllowForCasRandom | CasParamFlags.AllowForLiveRandom
            : flags & ~(CasParamFlags.AllowForCasRandom | CasParamFlags.AllowForLiveRandom);
        var singleGender = settings.Female ^ settings.Male;
        flags = singleGender ? flags | CasParamFlags.RestrictOppositeGender : flags & ~CasParamFlags.RestrictOppositeGender;
        buffer[part.FlagsOffset] = (byte)flags;

        if (part.Version >= 39)
        {
            var flags2 = singleGender
                ? part.Flags2 | CasParamFlags2.RestrictOppositeFrame
                : part.Flags2 & ~CasParamFlags2.RestrictOppositeFrame;
            buffer[part.FlagsOffset + 1] = (byte)flags2;
        }

        // Категории одежды — метки. Их число меняется, поэтому собираем блок меток заново.
        var tags = part.Tags
            .Where(t => !(t.Category == OutfitTags.TagCategory && OutfitTags.IsKnown(t.Value)))
            .ToList();
        foreach (var value in OutfitTags.Known.Select(k => k.Value).Where(settings.Outfits.Contains))
            tags.Add(new CasTag(OutfitTags.TagCategory, value));

        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(tags.Count);
        foreach (var t in tags)
        {
            w.Write(t.Category);
            if (part.Version >= 37) w.Write(t.Value); else w.Write((ushort)t.Value);
        }
        var newTags = ms.ToArray();
        var oldLength = part.TagsEndOffset - part.TagsOffset;
        var delta = newTags.Length - oldLength;

        var result = new byte[buffer.Length + delta];
        buffer.AsSpan(0, part.TagsOffset).CopyTo(result);
        newTags.CopyTo(result.AsSpan(part.TagsOffset));
        buffer.AsSpan(part.TagsEndOffset).CopyTo(result.AsSpan(part.TagsOffset + newTags.Length));

        // В начале ресурса записано, где лежит список ссылок (он после меток) — сдвигаем.
        var tgiOffset = BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(4));
        if (tgiOffset + 8 >= part.TagsEndOffset)
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)(tgiOffset + delta));

        return result;
    }
}
