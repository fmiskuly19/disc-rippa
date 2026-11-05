using System.Globalization;
using System.Text.Json;
using System.Text;

namespace DiscRippa.Core.Video;

/// <summary>Names that identify a particular disc in a particular drive.</summary>
public readonly record struct DiscKey(string Label, string Device);
