using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Windows;
using LumoraGo.Plugin;

namespace LumoraGo.App.Services;

/// <summary>Sürüklenen sonucu Windows'un anladığı biçimlere çevirir: dosya listesi, bağlantı ya da düz metin.</summary>
internal static class DragDataObject
{
    public static (DataObject Data, DragDropEffects Effects)? Build(DragPayload payload)
    {
        var data = new DataObject();
        switch (payload.Kind)
        {
            case DragKind.File:
                if (!File.Exists(payload.Value) && !Directory.Exists(payload.Value))
                {
                    return null;
                }

                data.SetFileDropList(new StringCollection { payload.Value });
                data.SetText(payload.Value, TextDataFormat.UnicodeText);
                return (data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);

            case DragKind.Url:
                data.SetText(payload.Value, TextDataFormat.UnicodeText);

                // Tarayıcılar ve Gezgin bağlantıyı bu biçimlerden tanır (Gezgin'e bırakınca .url kısayolu olur).
                data.SetData("UniformResourceLocatorW", new MemoryStream(Encoding.Unicode.GetBytes(payload.Value + "\0")));
                data.SetData("UniformResourceLocator", new MemoryStream(Encoding.Default.GetBytes(payload.Value + "\0")));
                return (data, DragDropEffects.Copy | DragDropEffects.Link);

            default:
                data.SetText(payload.Value, TextDataFormat.UnicodeText);
                return (data, DragDropEffects.Copy);
        }
    }
}
