using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Json.Schema;

namespace Gts.Store.Validation;

internal static class GtsFormatRegistry
{
    internal static FormatRegistry Create()
    {
        var registry = new FormatRegistry();
        registry.Register(new PredicateFormat("uuid", value => Guid.TryParseExact(value, "D", out _)));
        registry.Register(new PredicateFormat("email", IsEmail));
        registry.Register(new PredicateFormat("date-time", IsDateTime));
        registry.Register(new PredicateFormat("date", value => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)));
        registry.Register(new PredicateFormat("time", IsTime));
        registry.Register(new PredicateFormat("uri", value => Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Scheme)));
        registry.Register(new PredicateFormat("hostname", value => Uri.CheckHostName(value) == UriHostNameType.Dns));
        registry.Register(new PredicateFormat("ipv4", value => IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetwork));
        registry.Register(new PredicateFormat("ipv6", value => IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6));
        registry.Register(new PredicateFormat("regex", IsRegex));
        return registry;
    }

    private static bool IsEmail(string value)
    {
        try
        {
            var address = new MailAddress(value);
            return address.Address == value && value.Contains('@');
        }
        catch
        {
            return false;
        }
    }

    private static bool IsDateTime(string value) =>
        Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}[Tt]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:[Zz]|[+-]\d{2}:\d{2})$") &&
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static bool IsTime(string value)
    {
        var match = Regex.Match(value, @"^(\d{2}):(\d{2}):(\d{2})(?:\.\d+)?(?:[Zz]|([+-])(\d{2}):(\d{2}))$");
        if (!match.Success || int.Parse(match.Groups[1].Value) > 23 || int.Parse(match.Groups[2].Value) > 59 || int.Parse(match.Groups[3].Value) > 59)
            return false;
        return !match.Groups[5].Success || int.Parse(match.Groups[5].Value) <= 23 && int.Parse(match.Groups[6].Value) <= 59;
    }

    private static bool IsRegex(string value)
    {
        try
        {
            _ = new Regex(value, RegexOptions.ECMAScript);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class PredicateFormat(string key, Func<string, bool> predicate) : Format(key)
    {
        public override bool Validate(JsonElement value, out string? errorMessage)
        {
            var valid = value.ValueKind != JsonValueKind.String || predicate(value.GetString()!);
            errorMessage = valid ? null : $"Value is not a valid '{Key}' format";
            return valid;
        }
    }
}