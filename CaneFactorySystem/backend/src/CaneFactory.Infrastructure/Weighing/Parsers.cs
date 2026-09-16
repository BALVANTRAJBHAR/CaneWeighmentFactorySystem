using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CaneFactory.Application.DTOs;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;

namespace CaneFactory.Infrastructure.Weighing;

public static class ParserFactory
{
    public static IWeightFrameParser Create(StringProfile p) => p.ParserType switch
    {
        "FixedPosition" => new FixedPositionParser(p),
        "Delimiter" => new DelimiterParser(p),
        "Regex" => new RegexParser(p),
        "KeyValue" => new KeyValueParser(p),
        "Json" => new JsonParser(p),
        _ => new FixedPositionParser(p)
    };

    public static byte? HexByte(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return byte.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b) ? b : null;
    }
}

public abstract class ParserBase : IWeightFrameParser
{
    protected readonly StringProfile P;
    protected ParserBase(StringProfile p) => P = p;

    public ParsedFrameDto Parse(byte[] frame)
    {
        var dto = new ParsedFrameDto
        {
            RawHex = string.Join(" ", frame.Select(b => b.ToString("X2"))),
            WeightUnit = P.WeightUnit
        };
        try
        {
            if (P.FrameValidation && !ValidateFrame(frame))
            {
                dto.FrameValid = false;
                dto.Error = "Frame validation failed (start/end byte mismatch or wrong length).";
                return dto;
            }
            ParseCore(frame, dto);
            if (dto.NumericWeight.HasValue && P.DecimalPlaces > 0 && P.ParserType == "FixedPosition")
                dto.NumericWeight = Math.Round(dto.NumericWeight.Value, P.DecimalPlaces);
            dto.FrameValid = dto.NumericWeight.HasValue;
            if (!dto.FrameValid && dto.Error == null) dto.Error = "Weight could not be parsed from frame.";
        }
        catch (Exception ex)
        {
            dto.FrameValid = false;
            dto.Error = $"Parser error: {ex.Message}";
        }
        return dto;
    }

    protected virtual bool ValidateFrame(byte[] frame)
    {
        var start = ParserFactory.HexByte(P.StartByte);
        var end = ParserFactory.HexByte(P.EndByte);
        if (start.HasValue && (frame.Length == 0 || frame[0] != start.Value)) return false;
        if (end.HasValue && !frame.Contains(end.Value)) return false;
        return true;
    }

    protected abstract void ParseCore(byte[] frame, ParsedFrameDto dto);

    protected void ApplySign(ParsedFrameDto dto, string signChar)
    {
        var neg = ParserFactory.HexByte(P.NegativeSignValue);
        dto.Sign = neg.HasValue && signChar.Length == 1 && (byte)signChar[0] == neg.Value ? "-" : "+";
        if (dto.Sign == "-" && dto.NumericWeight.HasValue) dto.NumericWeight = -dto.NumericWeight;
    }

    protected decimal? ToDecimal(string chars)
    {
        chars = chars.Trim();
        if (chars.Length == 0) return null;
        if (P.WeightCharacterOrder == "Reversed") chars = new string(chars.Reverse().ToArray());
        if (!decimal.TryParse(chars, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)) return null;
        if (P.DecimalPlaces > 0 && !chars.Contains('.')) v /= (decimal)Math.Pow(10, P.DecimalPlaces);
        return v;
    }
}

/// <summary>Fixed byte-position parser. Positions are 1-based over the whole frame including STX. Handles String Type 15.</summary>
public class FixedPositionParser : ParserBase
{
    public FixedPositionParser(StringProfile p) : base(p) { }

    protected override bool ValidateFrame(byte[] frame)
    {
        if (!base.ValidateFrame(frame)) return false;
        return frame.Length >= P.WeightStartPosition - 1 + P.WeightLength;
    }

    protected override void ParseCore(byte[] frame, ParsedFrameDto dto)
    {
        var text = Encoding.ASCII.GetString(frame);
        var chars = text.Substring(P.WeightStartPosition - 1, P.WeightLength);
        dto.WeightCharacters = chars;
        dto.NumericWeight = ToDecimal(chars.Replace(' ', '0'));
        if (P.SignPosition.HasValue && P.SignPosition.Value <= text.Length)
            ApplySign(dto, text[P.SignPosition.Value - 1].ToString());
        else dto.Sign = "+";
    }
}

public class DelimiterParser : ParserBase
{
    public DelimiterParser(StringProfile p) : base(p) { }
    protected override void ParseCore(byte[] frame, ParsedFrameDto dto)
    {
        var text = Encoding.ASCII.GetString(frame).Trim('\x02', '\x03', '\r', '\n');
        var parts = text.Split(P.Delimiter ?? ",");
        var idx = Math.Max(0, P.WeightStartPosition - 1);
        if (idx >= parts.Length) { dto.Error = "Delimiter field index out of range."; return; }
        dto.WeightCharacters = parts[idx].Trim();
        dto.NumericWeight = ToDecimal(dto.WeightCharacters);
        dto.Sign = dto.NumericWeight < 0 ? "-" : "+";
    }
}

public class RegexParser : ParserBase
{
    public RegexParser(StringProfile p) : base(p) { }
    protected override void ParseCore(byte[] frame, ParsedFrameDto dto)
    {
        var text = Encoding.ASCII.GetString(frame);
        var m = Regex.Match(text, P.RegexPattern ?? @"(?<weight>-?\d+(\.\d+)?)");
        if (!m.Success) { dto.Error = "Regex did not match frame."; return; }
        dto.WeightCharacters = m.Groups["weight"].Value;
        dto.NumericWeight = ToDecimal(dto.WeightCharacters);
        dto.Sign = m.Groups["sign"].Success ? m.Groups["sign"].Value : (dto.NumericWeight < 0 ? "-" : "+");
    }
}

public class KeyValueParser : ParserBase
{
    public KeyValueParser(StringProfile p) : base(p) { }
    protected override void ParseCore(byte[] frame, ParsedFrameDto dto)
    {
        var text = Encoding.ASCII.GetString(frame).Trim('\x02', '\x03', '\r', '\n');
        foreach (var pair in text.Split(P.Delimiter ?? ";"))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && kv[0].Trim().Equals(P.KeyName ?? "WT", StringComparison.OrdinalIgnoreCase))
            {
                dto.WeightCharacters = kv[1].Trim();
                dto.NumericWeight = ToDecimal(dto.WeightCharacters);
                dto.Sign = dto.NumericWeight < 0 ? "-" : "+";
                return;
            }
        }
        dto.Error = $"Key '{P.KeyName}' not found in frame.";
    }
}

public class JsonParser : ParserBase
{
    public JsonParser(StringProfile p) : base(p) { }
    protected override void ParseCore(byte[] frame, ParsedFrameDto dto)
    {
        var text = Encoding.ASCII.GetString(frame).Trim('\x02', '\x03', '\r', '\n');
        using var doc = JsonDocument.Parse(text);
        if (doc.RootElement.TryGetProperty(P.JsonPath ?? P.KeyName ?? "weight", out var el))
        {
            dto.WeightCharacters = el.ToString();
            dto.NumericWeight = ToDecimal(dto.WeightCharacters);
            dto.Sign = dto.NumericWeight < 0 ? "-" : "+";
        }
        else dto.Error = $"JSON property '{P.JsonPath ?? P.KeyName}' not found.";
    }
}
