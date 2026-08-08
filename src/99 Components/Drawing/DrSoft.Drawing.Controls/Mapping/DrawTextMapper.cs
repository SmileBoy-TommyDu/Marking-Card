using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using Riok.Mapperly.Abstractions;

namespace DrSoft.Drawing.Controls.Mapping;

[Mapper(UseDeepCloning = true)]  // ¿ªÆôÉî¿½±´
public static partial class DrawTextMapper
{
    [MapperIgnoreSource(nameof(FontSettings.TextColor))]
    public static partial FontSettingsDto Map(FontSettings source);

    [MapperIgnoreTarget(nameof(FontSettings.TextColor))]
    public static partial FontSettings Map(FontSettingsDto source);

    public static partial FontSettings Clone(FontSettings fontSettings);
}
