using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Rules;
using StonesHotspot.Core.Services;
using StonesHotspot.Core.Sms;
using StonesHotspot.Tests.Infrastructure;

namespace StonesHotspot.Tests;

public class FreeAccessTests
{
    private static readonly TimeZoneInfo Congo = TimeZoneInfo.CreateCustomTimeZone("WAT", TimeSpan.FromHours(1), "WAT", "WAT");
    private const string Mtn = "+242061234567";

    private static async Task SetAsync(TestHost host, params (string Key, string Value)[] values)
    {
        foreach (var (key, value) in values)
            await host.Run<SettingsService>(s => s.SetAsync(key, value, null));
    }

    /// <summary>A 15-minute plan whose time runs from the first login, so it ends on its own.</summary>
    private static async Task<Tariff> FreePlanAsync(TestHost host) =>
        await host.CreateTariffAsync(quota: 15, price: 0, mode: CountdownMode.WallClock, name: "Gratuit 15 min");

    private static Task<LoginResult> TermsAsync(TestHost host, string mac = TestHost.PhoneMac) =>
        host.Run<FreeAccessService, LoginResult>(s => s.LoginWithTermsAsync(mac, "192.168.137.10"));

    private static Task<SmsCodeResult> AskCodeAsync(TestHost host, string phone = "06 123 45 67", string mac = TestHost.PhoneMac) =>
        host.Run<SmsLoginService, SmsCodeResult>(s => s.RequestCodeAsync(mac, "192.168.137.10", phone));

    private static Task<LoginResult> VerifyAsync(TestHost host, string code, string phone = Mtn, string mac = TestHost.PhoneMac) =>
        host.Run<SmsLoginService, LoginResult>(s => s.VerifyAsync(mac, "192.168.137.10", phone, code));

    private static async Task<TestHost> SmsHostAsync()
    {
        var host = await TestHost.CreateAsync();
        var plan = await FreePlanAsync(host);
        await SetAsync(host, (SettingKeys.SmsEnabled, "true"), (SettingKeys.SmsTariffId, $"{plan.Id}"), (SettingKeys.SmsTransport, "http"));
        await host.Run<SmsService>(s => s.SaveProviderAsync("https://sms.example/send?to={phone}&text={message}", null, null));
        return host;
    }

    [Theory]
    [InlineData("06 123 45 67", "+242061234567")]
    [InlineData("061234567", "+242061234567")]
    [InlineData("+242 06 123 45 67", "+242061234567")]
    [InlineData("242-06-123-45-67", "+242061234567")]
    [InlineData("00242 05 555 12 34", "+242055551234")]
    [InlineData("(+242) 04.444.44.44", "+242044444444")]
    [InlineData("6 123 45 67", null)]
    [InlineData("+33 6 12 34 56 78", null)]
    [InlineData("06 123 45 6x", null)]
    [InlineData("06+123 45 67", null)]
    [InlineData("", null)]
    public void Congo_numbers_are_normalized(string input, string? expected) => Assert.Equal(expected, PhoneNumber.Normalize(input));

    [Fact]
    public void Number_prefixes()
    {
        var prefixes = PhoneNumber.ParsePrefixes("+24206, 05; 24204 x7 ");
        Assert.Equal(["+24206", "+24205", "+24204"], prefixes);
        Assert.True(PhoneNumber.IsAllowed("+242055551234", prefixes));
        Assert.False(PhoneNumber.IsAllowed("+242011234567", prefixes));
        Assert.True(PhoneNumber.IsAllowed("+242011234567", []));
        Assert.Equal("+242 06 123 45 67", PhoneNumber.Format(Mtn));
    }

    [Fact]
    public async Task Daily_password_changes_at_local_midnight()
    {
        await using var host = await TestHost.CreateAsync();
        host.Clock.SetLocalTimeZone(Congo);
        await SetAsync(host, (SettingKeys.PasswordValue, "bienvenue"));
        Assert.Equal("bienvenue", await host.Run<FreeAccessService, string?>(s => s.PasswordOfTheDayAsync()));

        await SetAsync(host, (SettingKeys.PasswordDaily, "true"));
        host.Clock.SetUtcNow(new DateTimeOffset(2026, 3, 2, 22, 50, 0, TimeSpan.Zero));   // 23:50 local
        var evening = await host.Run<FreeAccessService, string?>(s => s.PasswordOfTheDayAsync());
        host.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(evening, await host.Run<FreeAccessService, string?>(s => s.PasswordOfTheDayAsync()));
        host.Clock.Advance(TimeSpan.FromMinutes(10));                                        // 00:05 local, next day
        var morning = await host.Run<FreeAccessService, string?>(s => s.PasswordOfTheDayAsync());

        Assert.NotEqual(evening, morning);
        Assert.Matches("^[A-HJ-NP-Z2-9]{6}$", morning!);
        Assert.Equal(FreeAccessService.DailyPassword("s", new DateOnly(2026, 1, 1)), FreeAccessService.DailyPassword("s", new DateOnly(2026, 1, 1)));
        Assert.NotEqual(FreeAccessService.DailyPassword("s", new DateOnly(2026, 1, 1)), FreeAccessService.DailyPassword("t", new DateOnly(2026, 1, 1)));
        var secret = await host.Query(db => db.AuditLogs.Where(a => a.Details!.Contains(SettingKeys.PasswordSecret)).Select(a => a.Details).SingleAsync());
        Assert.Contains("(secret changed)", secret);
    }

    [Fact]
    public async Task Free_access_gives_one_voucher_per_device_and_day()
    {
        await using var host = await TestHost.CreateAsync();
        host.Clock.SetLocalTimeZone(Congo);
        var plan = await FreePlanAsync(host);

        Assert.Equal(LoginStatus.ModeDisabled, (await TermsAsync(host)).Status);
        await SetAsync(host, (SettingKeys.FreeEnabled, "true"), (SettingKeys.FreeTariffId, $"{plan.Id}"));

        var first = await TermsAsync(host);
        Assert.True(first.Succeeded);
        Assert.Equal(LoginMethod.Free, first.Session!.Method);
        var voucher = await host.Query(db => db.Vouchers.SingleAsync());
        Assert.Equal((VoucherSource.Free, plan.Id), (voucher.Source, voucher.TariffId));
        Assert.NotNull(voucher.IssuedToDeviceId);

        // Log out and in again: same free voucher, nothing used up.
        await host.Run<SessionService, bool>(s => s.EndAsync(first.Session.Id, SessionEndReason.Logout));
        Assert.True((await TermsAsync(host)).Succeeded);
        Assert.Equal(1, await host.Query(db => db.Vouchers.CountAsync()));

        // The 15 minutes run out: no second free login today (limit 1), another device still can.
        host.Advance(TimeSpan.FromMinutes(16));
        await host.TickAsync();
        Assert.Equal(LoginStatus.DailyLimitReached, (await TermsAsync(host)).Status);
        Assert.True((await TermsAsync(host, TestHost.LaptopMac)).Succeeded);

        // Tomorrow (local day) it works again.
        host.Clock.SetUtcNow(new DateTimeOffset(2026, 3, 2, 23, 1, 0, TimeSpan.Zero));
        Assert.True((await TermsAsync(host)).Succeeded);

        // Free vouchers stay out of the batches staff see.
        Assert.Empty(await host.Run<VoucherService, List<BatchSummary>>(s => s.ListBatchesAsync()));
    }

    [Fact]
    public async Task Shared_password_login_and_lock_out()
    {
        await using var host = await TestHost.CreateAsync();
        var plan = await FreePlanAsync(host);
        await SetAsync(host, (SettingKeys.PasswordEnabled, "true"), (SettingKeys.PasswordTariffId, $"{plan.Id}"), (SettingKeys.PasswordValue, "Bienvenue"));
        Task<LoginResult> Login(string password, string mac = TestHost.PhoneMac) =>
            host.Run<FreeAccessService, LoginResult>(s => s.LoginWithPasswordAsync(mac, null, password));

        Assert.Equal(LoginStatus.InvalidPassword, (await Login("bienvenu")).Status);
        var ok = await Login(" BIENVENUE ");
        Assert.True(ok.Succeeded);
        Assert.Equal(LoginMethod.SharedPassword, ok.Session!.Method);

        for (var i = 0; i < 4; i++)
            await Login("faux", TestHost.LaptopMac);
        Assert.Equal(LoginStatus.LockedOut, (await Login("faux", TestHost.LaptopMac)).Status);
        Assert.Equal(LoginStatus.LockedOut, (await Login("Bienvenue", TestHost.LaptopMac)).Status);
    }

    [Fact]
    public async Task Sms_code_logs_in_with_the_number()
    {
        await using var host = await SmsHostAsync();

        var asked = await AskCodeAsync(host);
        Assert.Equal((SmsCodeStatus.Sent, Mtn), (asked.Status, asked.Phone));
        var (phone, text) = host.Sms.Sent.Single();
        Assert.Equal(Mtn, phone);
        var code = host.Sms.LastCode(Mtn);
        Assert.Contains($"code Wi-Fi est {code}", text);
        var challenge = await host.Query(db => db.SmsChallenges.SingleAsync());
        Assert.DoesNotContain(code, challenge.CodeHash);
        Assert.True(await host.Query(db => db.SmsMessages.AnyAsync(m => m.Sent && m.Purpose == SmsPurpose.LoginCode)));

        var wrong = code == "000000" ? "111111" : "000000";
        Assert.Equal(LoginStatus.InvalidCode, (await VerifyAsync(host, wrong)).Status);
        Assert.Equal(LoginStatus.InvalidCode, (await VerifyAsync(host, code, mac: TestHost.LaptopMac)).Status);   // code was sent for another device
        var ok = await VerifyAsync(host, code[..3] + " " + code[3..]);
        Assert.True(ok.Succeeded);
        Assert.Equal(LoginMethod.Sms, ok.Session!.Method);
        Assert.Equal(Mtn, (await host.Query(db => db.Vouchers.SingleAsync())).Phone);
        Assert.Equal(LoginStatus.InvalidCode, (await VerifyAsync(host, code)).Status);   // a code works once
    }

    [Fact]
    public async Task Sms_codes_expire_and_allow_five_tries()
    {
        await using var host = await SmsHostAsync();
        await AskCodeAsync(host);
        var code = host.Sms.LastCode(Mtn);
        host.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(LoginStatus.CodeExpired, (await VerifyAsync(host, code)).Status);

        await AskCodeAsync(host, mac: TestHost.LaptopMac);
        var second = host.Sms.LastCode(Mtn);
        await host.Query(db => db.SmsChallenges.Where(c => c.UsedAt == null).ExecuteUpdateAsync(u => u.SetProperty(c => c.Attempts, 5)));
        Assert.Equal(LoginStatus.CodeExpired, (await VerifyAsync(host, second, mac: TestHost.LaptopMac)).Status);   // 5 tries used
    }

    [Fact]
    public async Task Sms_sending_limits_protect_the_credit()
    {
        await using var host = await SmsHostAsync();
        await SetAsync(host, (SettingKeys.SmsPerNumberPerDay, "2"), (SettingKeys.SmsPerDevicePerDay, "3"), (SettingKeys.SmsTotalPerDay, "4"));

        Assert.Equal(SmsCodeStatus.InvalidNumber, (await AskCodeAsync(host, "1234")).Status);
        Assert.Equal(SmsCodeStatus.PrefixNotAllowed, (await AskCodeAsync(host, "01 234 56 78")).Status);

        Assert.Equal(SmsCodeStatus.Sent, (await AskCodeAsync(host)).Status);
        var soon = await AskCodeAsync(host);
        Assert.Equal(SmsCodeStatus.TooSoon, soon.Status);
        Assert.InRange(soon.WaitSeconds!.Value, 1, 60);
        host.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(SmsCodeStatus.Sent, (await AskCodeAsync(host)).Status);
        host.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(SmsCodeStatus.NumberLimit, (await AskCodeAsync(host)).Status);            // 2 per number
        Assert.Equal(SmsCodeStatus.Sent, (await AskCodeAsync(host, "05 555 12 34")).Status);
        host.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(SmsCodeStatus.DeviceLimit, (await AskCodeAsync(host, "04 444 44 44")).Status); // 3 per device
        Assert.Equal(SmsCodeStatus.Sent, (await AskCodeAsync(host, "04 444 44 44", TestHost.LaptopMac)).Status);
        Assert.Equal(SmsCodeStatus.TotalLimit, (await AskCodeAsync(host, "06 999 99 99", TestHost.TabletMac)).Status); // 4 in total
        Assert.Equal(4, host.Sms.Sent.Count);

        host.Advance(TimeSpan.FromDays(1));
        Assert.Equal(SmsCodeStatus.Sent, (await AskCodeAsync(host)).Status);
    }

    [Fact]
    public async Task Failed_sms_is_logged_and_no_code_is_kept()
    {
        await using var host = await SmsHostAsync();
        host.Sms.FailWith = "The modem answered +CMS ERROR: 500.";

        var result = await AskCodeAsync(host);

        Assert.Equal(SmsCodeStatus.SendFailed, result.Status);
        Assert.Contains("CMS ERROR", result.Error);
        Assert.Equal(0, await host.Query(db => db.SmsChallenges.CountAsync()));
        var log = await host.Query(db => db.SmsMessages.SingleAsync());
        Assert.False(log.Sent);
        Assert.Contains("CMS ERROR", log.Error);
    }

    [Fact]
    public async Task Sms_mode_needs_a_plan_and_the_daily_login_limit_applies_before_sending()
    {
        await using var host = await SmsHostAsync();
        Assert.Equal(SmsCodeStatus.Sent, (await AskCodeAsync(host)).Status);
        Assert.True((await VerifyAsync(host, host.Sms.LastCode(Mtn))).Succeeded);
        host.Advance(TimeSpan.FromMinutes(16));
        await host.TickAsync();

        Assert.Equal(SmsCodeStatus.DailyLoginLimit, (await AskCodeAsync(host)).Status);   // 1 SMS login per device per day
        Assert.Single(host.Sms.Sent);

        await SetAsync(host, (SettingKeys.SmsTariffId, ""));
        Assert.Equal(SmsCodeStatus.ModeDisabled, (await AskCodeAsync(host, mac: TestHost.LaptopMac)).Status);
    }

    [Fact]
    public async Task Provider_address_is_stored_encrypted()
    {
        await using var host = await TestHost.CreateAsync();
        const string url = "https://api.example.com/send?key=SECRET123&to={phone}&text={message}";
        await host.Run<SmsService>(s => s.SaveProviderAsync(url, null, null));

        var stored = await host.Query(db => db.Settings.SingleAsync(s => s.Key == SettingKeys.SmsHttpUrl));
        Assert.DoesNotContain("SECRET123", stored.Value);
        Assert.Equal(url, (await host.Run<SmsService, SmsSettings>(s => s.GetSettingsAsync())).HttpUrl);
        Assert.DoesNotContain(await host.Query(db => db.AuditLogs.Select(a => a.Details ?? "").ToListAsync()), d => d.Contains("SECRET123"));
        Assert.Equal("+24206****567", SmsService.Mask(Mtn));
    }

    private sealed class RecordingHandler(HttpStatusCode status, string answer) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(answer) };
        }
    }

    [Fact]
    public async Task Http_provider_fills_the_templates()
    {
        var get = new RecordingHandler(HttpStatusCode.OK, "OK: queued");
        await new HttpSmsTransport(new HttpClient(get), "https://sms.example/send?to={phone_digits}&msg={message}", null, "queued")
            .SendAsync(Mtn, "Code : 123 456 & bonne journée", CancellationToken.None);
        Assert.Equal(HttpMethod.Get, get.Request!.Method);
        Assert.Equal("https://sms.example/send?to=242061234567&msg=Code%20%3A%20123%20456%20%26%20bonne%20journ%C3%A9e", get.Request.RequestUri!.AbsoluteUri);

        var post = new RecordingHandler(HttpStatusCode.Created, "{}");
        await new HttpSmsTransport(new HttpClient(post), "https://sms.example/v1/messages", "{\"to\":\"{phone}\",\"text\":\"{message}\"}", null)
            .SendAsync(Mtn, "Il a dit \"oui\"", CancellationToken.None);
        Assert.Equal(HttpMethod.Post, post.Request!.Method);
        Assert.Equal("application/json", post.Request.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("{\"to\":\"+242061234567\",\"text\":\"Il a dit \\\"oui\\\"\"}", post.Body);

        var refused = new RecordingHandler(HttpStatusCode.Unauthorized, "bad key");
        var ex = await Assert.ThrowsAsync<SmsException>(() =>
            new HttpSmsTransport(new HttpClient(refused), "https://sms.example/send?to={phone}", null, null).SendAsync(Mtn, "x", CancellationToken.None));
        Assert.Contains("401", ex.Message);
        await Assert.ThrowsAsync<SmsException>(() =>
            new HttpSmsTransport(new HttpClient(get), "https://sms.example/send", null, "SUCCESS").SendAsync(Mtn, "x", CancellationToken.None));
    }

    /// <summary>Answers AT commands like a GSM modem with echo on.</summary>
    private sealed class FakeModem(Func<string, string> answer) : Stream
    {
        private readonly System.Threading.Channels.Channel<byte[]> _out = System.Threading.Channels.Channel.CreateUnbounded<byte[]>();
        private readonly StringBuilder _line = new();
        private byte[] _pending = [];
        public List<string> Received { get; } = [];
        public string? MessageText { get; private set; }

        public override void Write(byte[] buffer, int offset, int count)
        {
            foreach (var b in buffer.AsSpan(offset, count))
            {
                if (b == 0x1A)
                {
                    MessageText = _line.ToString();
                    _line.Clear();
                    Reply(answer("<text>"));
                }
                else if (b == '\r')
                {
                    var command = _line.ToString();
                    _line.Clear();
                    Received.Add(command);
                    Reply(command + "\r" + answer(command));
                }
                else
                {
                    _line.Append((char)b);
                }
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            Write(buffer, offset, count);
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            Write(buffer.ToArray(), 0, buffer.Length);
            return ValueTask.CompletedTask;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_pending.Length == 0)
                _pending = await _out.Reader.ReadAsync(ct);
            var n = Math.Min(buffer.Length, _pending.Length);
            _pending.AsSpan(0, n).CopyTo(buffer.Span);
            _pending = _pending[n..];
            return n;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

        private void Reply(string text)
        {
            if (text.Length > 0)
                _out.Writer.TryWrite(Encoding.ASCII.GetBytes(text));
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    }

    private static string NormalModem(string command) => command switch
    {
        "AT+CPIN?" => "\r\n+CPIN: READY\r\n\r\nOK\r\n",
        var c when c.StartsWith("AT+CMGS=", StringComparison.Ordinal) => "\r\n> ",
        "<text>" => "\r\n+CMGS: 17\r\n\r\nOK\r\n",
        _ => "\r\nOK\r\n",
    };

    [Fact]
    public async Task Modem_sends_in_text_mode()
    {
        var modem = new FakeModem(NormalModem);

        await AtModem.SendSmsAsync(modem, Mtn, "Café : votre code est 123456", TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(["AT", "AT+CPIN?", "AT+CMGF=1", $"AT+CMGS=\"{Mtn}\""], modem.Received);
        Assert.Equal("Cafe : votre code est 123456", modem.MessageText);
    }

    [Fact]
    public async Task Modem_problems_are_explained()
    {
        var pin = new FakeModem(c => c == "AT+CPIN?" ? "\r\n+CPIN: SIM PIN\r\n\r\nOK\r\n" : NormalModem(c));
        Assert.Contains("PIN", (await Assert.ThrowsAsync<SmsException>(() => AtModem.SendSmsAsync(pin, Mtn, "x", TimeSpan.FromSeconds(5), CancellationToken.None))).Message);

        var noCredit = new FakeModem(c => c == "<text>" ? "\r\n+CMS ERROR: 38\r\n" : NormalModem(c));
        Assert.Contains("+CMS ERROR: 38", (await Assert.ThrowsAsync<SmsException>(() => AtModem.SendSmsAsync(noCredit, Mtn, "x", TimeSpan.FromSeconds(5), CancellationToken.None))).Message);

        var silent = new FakeModem(c => c == "<text>" ? "" : NormalModem(c));
        Assert.Contains("did not answer", (await Assert.ThrowsAsync<SmsException>(() => AtModem.SendSmsAsync(silent, Mtn, "x", TimeSpan.FromMilliseconds(300), CancellationToken.None))).Message);
    }

    [Fact]
    public void Sms_text_is_made_safe_for_any_modem() =>
        Assert.Equal("Stone's Hotspot : code \"123456\" - valable 10 min. ?", AtModem.ToGsmSafe("Stone’s Hotspot : code «123456» – valable 10 min. €"));
}
