using System;
using System.Collections.Generic;
using System.Linq;
namespace DecodingGif.Core.Models;
public sealed record GifHeader(string Signature, string Version)
{
    public static bool IsSupported(string signature, string version) =>
        signature == "GIF" && (version == "87a" || version == "89a");
}
