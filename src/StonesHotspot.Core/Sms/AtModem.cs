using System.Globalization;
using System.Text;

namespace StonesHotspot.Core.Sms;

/// <summary>
/// The AT-command conversation that sends one SMS in text mode through a GSM modem (USB key with a SIM card):
/// <c>AT</c>, <c>AT+CPIN?</c> (the SIM PIN must be disabled), <c>AT+CMGF=1</c>, <c>AT+CMGS="+242…"</c>, the text,
/// Ctrl-Z, then wait for <c>+CMGS</c>. Works on any stream so it can be tested with a fake modem.
/// </summary>
public static class AtModem
{
    private const byte CtrlZ = 0x1A;

    public static async Task SendSmsAsync(Stream port, string phone, string text, TimeSpan timeout, CancellationToken ct)
    {
        var reader = new ModemReader(port);
        async Task<string> Command(string command, string success, TimeSpan wait)
        {
            await port.WriteAsync(Encoding.ASCII.GetBytes(command + "\r"), ct);
            await port.FlushAsync(ct);
            return await reader.WaitForAsync(success, wait, ct);
        }

        await Command("AT", "OK", TimeSpan.FromSeconds(5));
        var pin = await Command("AT+CPIN?", "OK", TimeSpan.FromSeconds(5));
        if (!pin.Contains("READY", StringComparison.Ordinal))
            throw new SmsException("The SIM card asks for its PIN code: put the SIM in a phone and disable the PIN, then try again.");
        await Command("AT+CMGF=1", "OK", TimeSpan.FromSeconds(5));
        await Command($"AT+CMGS=\"{phone}\"", ">", TimeSpan.FromSeconds(10));
        byte[] message = [.. Encoding.ASCII.GetBytes(ToGsmSafe(text)), CtrlZ];
        await port.WriteAsync(message, ct);
        await port.FlushAsync(ct);
        await reader.WaitForAsync("+CMGS", timeout, ct);
    }

    /// <summary>Plain ASCII without control characters (accents removed): every modem sends it unchanged, at most 160 characters.</summary>
    public static string ToGsmSafe(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(c switch
            {
                '’' or '‘' => '\'',
                '«' or '»' or '“' or '”' => '"',
                ' ' or ' ' or '\n' or '\r' => ' ',
                '–' or '—' => '-',
                _ => c is >= ' ' and <= '~' && c is not ('[' or ']' or '{' or '}' or '\\' or '^' or '~' or '|' or '`') ? c : '?',
            });
        }
        var result = sb.ToString();
        return result.Length > 160 ? result[..160] : result;
    }

    /// <summary>Reads modem answers until one of the expected words, an error, or the time limit.</summary>
    private sealed class ModemReader(Stream port)
    {
        private readonly StringBuilder _buffer = new();

        /// <summary>
        /// Waits for <paramref name="expected"/> followed by the final "OK" (or just the "&gt;" prompt of AT+CMGS).
        /// </summary>
        public async Task<string> WaitForAsync(string expected, TimeSpan timeout, CancellationToken ct)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(timeout);
            var bytes = new byte[256];
            try
            {
                while (true)
                {
                    var text = _buffer.ToString();
                    if (Find(text, "+CMS ERROR", "+CME ERROR", "\r\nERROR", "ERROR\r") is { } error)
                    {
                        _buffer.Clear();
                        throw new SmsException($"The modem answered {Line(text, error)}.");
                    }
                    var at = text.IndexOf(expected, StringComparison.Ordinal);
                    if (at >= 0 && (expected == ">" || text.IndexOf("OK", at + (expected == "OK" ? 0 : expected.Length), StringComparison.Ordinal) >= 0))
                    {
                        _buffer.Clear();
                        return text;
                    }
                    var read = await port.ReadAsync(bytes, limit.Token);
                    if (read == 0)
                        throw new SmsException("The modem closed the connection.");
                    _buffer.Append(Encoding.ASCII.GetString(bytes, 0, read));
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new SmsException($"The modem did not answer within {timeout.TotalSeconds:0} s (is it the right COM port?).");
            }
        }

        private static int? Find(string text, params string[] words) =>
            words.Select(w => text.IndexOf(w, StringComparison.Ordinal)).Where(i => i >= 0).Cast<int?>().FirstOrDefault();

        private static string Line(string text, int at)
        {
            var start = at;
            while (start < text.Length && text[start] is '\r' or '\n')
                start++;
            var end = text.IndexOfAny(['\r', '\n'], start);
            return text[start..(end < 0 ? text.Length : end)].Trim();
        }
    }
}
