using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Manajoy.GameClass.Bridge.Setup;

namespace Manajoy.GameClass.Bridge.Tests;

public class LaunchModeTests
{
    [Fact]
    public void Service_start_runs_the_bridge()
    {
        var mode = LaunchMode.Parse([], isWindowsService: true);
        Assert.True(mode.RunBridge);
        Assert.Null(mode.Setup);
    }

    [Fact]
    public void Double_click_opens_the_setup_window()
    {
        var mode = LaunchMode.Parse([], isWindowsService: false);
        Assert.False(mode.RunBridge);
        Assert.NotNull(mode.Setup);
        Assert.False(mode.Setup!.Demo);
        Assert.False(mode.Setup.Headless);
    }

    [Fact]
    public void Once_and_run_switches_keep_the_bridge_and_are_stripped()
    {
        var once = LaunchMode.Parse(["--once", "--Bridge:Server=x"], isWindowsService: false);
        Assert.True(once.Once);
        Assert.Equal(["--Bridge:Server=x"], once.BridgeArgs);
        Assert.True(LaunchMode.Parse(["--run"], isWindowsService: false).RunBridge);
    }

    [Fact]
    public void Setup_switches_are_parsed()
    {
        var mode = LaunchMode.Parse(["--setup-demo", "--setup-headless", "--setup-port=18950", "--api-base=http://192.168.122.1:8099/api"], false);
        Assert.Equal(new SetupLaunchOptions(true, true, 18950, "http://192.168.122.1:8099/api"), mode.Setup);
    }

    [Theory]
    [InlineData("--setup-port=0")]
    [InlineData("--setup-port=99999")]
    [InlineData("--setup-port=abc")]
    public void Invalid_port_falls_back_to_a_random_port(string argument)
    {
        Assert.Null(LaunchMode.Parse([argument], false).Setup!.Port);
    }

    [Fact]
    public void Invalid_api_base_is_ignored()
    {
        Assert.Null(LaunchMode.Parse(["--api-base=not a url"], false).Setup!.ApiBase);
    }
}

public class SetupHelpersTests
{
    [Fact]
    public void Session_accepts_only_its_token_host_and_origin()
    {
        var session = new SetupSession("secret-token") { Port = 18950 };

        Assert.True(session.IsValidToken("secret-token"));
        Assert.False(session.IsValidToken("secret-tokem"));
        Assert.False(session.IsValidToken(null));
        Assert.True(session.IsAllowedHost("127.0.0.1:18950"));
        Assert.True(session.IsAllowedHost("localhost:18950"));
        Assert.False(session.IsAllowedHost("evil.example:18950"));
        Assert.False(session.IsAllowedHost("127.0.0.1:8787"));
        Assert.True(session.IsAllowedOrigin("http://127.0.0.1:18950"));
        Assert.False(session.IsAllowedOrigin("http://evil.example"));
    }

    [Theory]
    [InlineData("https://api.manajoy.uz/api/integrations/gameclass/26/snapshot", 26)]
    [InlineData("http://192.168.122.1:8099/api/integrations/gameclass/1/snapshot", 1)]
    [InlineData("https://api.manajoy.uz/api/integrations/gameclass/x/snapshot", null)]
    [InlineData(null, null)]
    public void Seller_id_is_read_from_the_push_url(string? url, int? expected)
    {
        Assert.Equal(expected, SetupEnvironment.SellerIdFromPushUrl(url));
    }

    [Fact]
    public void Status_url_sits_next_to_the_push_url()
    {
        Assert.Equal("https://api.manajoy.uz/api/integrations/gameclass/26/status",
            SetupEnvironment.StatusUrlFromPushUrl("https://api.manajoy.uz/api/integrations/gameclass/26/snapshot"));
        Assert.Null(SetupEnvironment.StatusUrlFromPushUrl("https://example.com/other"));
        Assert.Equal("https://api.manajoy.uz/api/integrations/gameclass/26/layout",
            SetupEnvironment.LayoutUrlFromPushUrl("https://api.manajoy.uz/api/integrations/gameclass/26/snapshot"));
        Assert.Null(SetupEnvironment.LayoutUrlFromPushUrl(null));
    }

    [Fact]
    public void Layout_conflict_body_carries_the_server_map()
    {
        var body = """{"success":false,"error":{"code":"layout_conflict","message":"x","server":{"grid":1000,"version":"v9","pcs":[{"name":"1","resource_id":5,"x":100,"y":200},{"name":"2","resource_id":6,"x":null,"y":null}],"other_placed":3}},"status_code":409}""";

        var server = ManajoyApiClient.ParseConflictServer(body);

        Assert.NotNull(server);
        Assert.Equal("v9", server!.Version);
        Assert.Equal(3, server.OtherPlaced);
        Assert.Equal(new RemotePc("1", 100, 200), server.Pcs[0]);
        Assert.Null(server.Pcs[1].X);
        Assert.Null(ManajoyApiClient.ParseConflictServer("""{"error":"plain"}"""));
    }

    [Fact]
    public void Prefs_keep_only_known_values()
    {
        var store = new PrefsStore(null);
        Assert.Equal(new SetupPrefs("ru", null), store.Save(new SetupPrefs("ru", "<script>")));
        Assert.Equal(new SetupPrefs("ru", "map"), store.Save(new SetupPrefs(null, "map")));
        Assert.Equal(new SetupPrefs(null, null), PrefsStore.Clean(new SetupPrefs("en", "grid", "wide")));
        Assert.Equal("closed", store.Save(new SetupPrefs(null, null, "closed")).Rail);
        Assert.Equal("ru", store.Load().Lang);
    }

    [Theory]
    [InlineData("""{"success":false,"error":{"code":"invalid_code","message":"Kod noto'g'ri"},"status_code":404}""", "invalid_code", "Kod noto'g'ri")]
    [InlineData("""{"success":false,"error":"Juda tez-tez","status_code":429}""", null, "Juda tez-tez")]
    [InlineData("""{"detail":{"code":"other_provider","message":"iCafe"}}""", "other_provider", "iCafe")]
    [InlineData("""{"detail":"Too many requests"}""", null, "Too many requests")]
    [InlineData("<html>502 Bad Gateway</html>", null, "<html>502 Bad Gateway</html>")]
    public void Backend_errors_are_parsed(string body, string? code, string message)
    {
        Assert.Equal((code, message), ManajoyApiClient.ParseError(body));
    }

    [Fact]
    public void Generated_login_password_is_long_and_unambiguous()
    {
        var passwords = Enumerable.Range(0, 50).Select(_ => ReadOnlyLoginProvisioner.GeneratePassword()).ToList();
        Assert.All(passwords, p => Assert.Matches("^[A-HJ-NP-Za-km-z2-9]{24}$", p));
        Assert.Equal(passwords.Count, passwords.Distinct().Count());
    }

    [Fact]
    public void Verdict_is_the_worst_level()
    {
        Assert.Equal(CheckLevel.Ok, Preflight.Verdict([new("a", CheckLevel.Ok), new("b", CheckLevel.Info)]));
        Assert.Equal(CheckLevel.Warn, Preflight.Verdict([new("a", CheckLevel.Ok), new("b", CheckLevel.Warn)]));
        Assert.Equal(CheckLevel.Fail, Preflight.Verdict([new("a", CheckLevel.Warn), new("b", CheckLevel.Fail)]));
    }
}

public class SetupUiResourceTests
{
    private static readonly Assembly BridgeAssembly = typeof(LaunchMode).Assembly;

    [Theory]
    [InlineData("SetupUi/index.html")]
    [InlineData("SetupUi/app.css")]
    [InlineData("SetupUi/app.js")]
    [InlineData("SetupUi/map.js")]
    [InlineData("SetupUi/reports.js")]
    [InlineData("SetupUi/bar.js")]
    [InlineData("SetupUi/i18n.js")]
    [InlineData("SetupUi/logo-white.png")]
    [InlineData("SetupUi/jakarta-latin.woff2")]
    [InlineData("appsettings.default.json")]
    public void Ui_files_are_embedded_in_the_exe(string name)
    {
        Assert.Contains(name, BridgeAssembly.GetManifestResourceNames());
    }

    [Fact]
    public void Uzbek_and_russian_texts_have_the_same_keys()
    {
        var script = Read("SetupUi/i18n.js");
        var ruStart = script.IndexOf("\n  ru: {", StringComparison.Ordinal);
        Assert.True(ruStart > 0);
        var uz = Keys(script[..ruStart]);
        var ru = Keys(script[ruStart..]);
        Assert.NotEmpty(uz);
        Assert.Empty(uz.Except(ru));
        Assert.Empty(ru.Except(uz));
    }

    [Fact]
    public void Ui_has_no_inline_script_or_style_that_the_csp_would_block()
    {
        var html = Read("SetupUi/index.html");
        Assert.DoesNotMatch("<script>|<style|style=|onclick=|onload=", html);
        Assert.Contains("<script src=\"map.js\"></script>", html);
        foreach (var script in new[] { "SetupUi/app.js", "SetupUi/map.js", "SetupUi/reports.js", "SetupUi/bar.js" })
        {
            Assert.DoesNotMatch(" style=\"| onclick=\"|setAttribute\\('style'", Read(script));
        }
    }

    private static HashSet<string> Keys(string block) =>
        Regex.Matches(block, @"^\s+'([a-zA-Z0-9_.]+)':", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToHashSet();

    private static string Read(string name)
    {
        using var stream = BridgeAssembly.GetManifestResourceStream(name)!;
        return new StreamReader(stream).ReadToEnd();
    }
}

/// <summary>Runs the real setup host (demo actions, headless) on a free port and drives it over HTTP.</summary>
[Collection("console")]
public class SetupHostDemoTests
{
    [Fact]
    public async Task Demo_host_guards_the_api_and_runs_the_whole_wizard()
    {
        var port = FreePort();
        var console = new StringWriter();
        var original = Console.Out;
        Console.SetOut(console);
        Task<int> host;
        try
        {
            host = SetupHost.RunAsync(new SetupLaunchOptions(Demo: true, Headless: true, Port: port, ApiBase: null));
            string? url = null;
            for (var i = 0; i < 100 && url is null; i++)
            {
                await Task.Delay(100);
                url = Regex.Match(console.ToString(), @"SETUP_URL=(\S+)").Groups[1].Value is { Length: > 0 } found ? found : null;
            }

            Assert.NotNull(url);
            var token = url![(url.IndexOf("#k=", StringComparison.Ordinal) + 3)..];
            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/api/setup/state")).StatusCode);

            http.DefaultRequestHeaders.Add("X-Setup-Token", token);
            using (var evil = new HttpRequestMessage(HttpMethod.Get, "/api/setup/state"))
            {
                evil.Headers.Host = "evil.example";
                Assert.Equal(HttpStatusCode.MisdirectedRequest, (await http.SendAsync(evil)).StatusCode);
            }

            using (var crossSite = new HttpRequestMessage(HttpMethod.Post, "/api/setup/preflight"))
            {
                crossSite.Headers.Add("Origin", "http://evil.example");
                Assert.Equal(HttpStatusCode.Forbidden, (await http.SendAsync(crossSite)).StatusCode);
            }

            var state = await http.GetFromJsonAsync<JsonElement>("/api/setup/state");
            Assert.True(state.GetProperty("demo").GetBoolean());
            Assert.False(state.GetProperty("installed").GetBoolean());

            var preflight = await http.PostAsync("/api/setup/preflight", null);
            Assert.Equal("warn", (await preflight.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("verdict").GetString());

            var install = await http.PostAsync("/api/setup/install", null);
            Assert.Equal(HttpStatusCode.BadRequest, install.StatusCode);
            Assert.Equal("database_step_required", (await install.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code").GetString());

            var database = await http.PostAsJsonAsync("/api/setup/database", new { adminUser = "sa", adminPassword = "", windowsAuth = false });
            Assert.True((await database.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("success").GetBoolean());

            install = await http.PostAsync("/api/setup/install", null);
            Assert.True((await install.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("success").GetBoolean());

            var wrongCode = await http.PostAsJsonAsync("/api/setup/pair", new { code = "ZZZZ-ZZZZ" });
            Assert.Equal("pair_invalid_code", (await wrongCode.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code").GetString());

            var pair = await http.PostAsJsonAsync("/api/setup/pair", new { code = "test-2345" });
            Assert.Equal("Cyber Arena", (await pair.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("clubName").GetString());

            var dashboard = await http.GetFromJsonAsync<JsonElement>("/api/setup/dashboard");
            Assert.True(dashboard.GetProperty("local").GetProperty("pcs").GetArrayLength() > 0);
            Assert.Equal("Cyber Arena", dashboard.GetProperty("manajoy").GetProperty("clubName").GetString());

            // Club map: the demo club is drawn and synced; PCs 23-24 are not Manajoy stations yet.
            var layout = await http.GetFromJsonAsync<JsonElement>("/api/setup/layout");
            Assert.Equal("synced", layout.GetProperty("sync").GetProperty("state").GetString());
            Assert.Equal(24, layout.GetProperty("layout").GetProperty("pcs").EnumerateObject().Count());
            Assert.Equal(1000, layout.GetProperty("grid").GetInt32());

            var saved = await http.PutAsJsonAsync("/api/setup/layout", new
            {
                pcs = new Dictionary<string, object> { ["1"] = new { x = 100, y = 100 }, ["23"] = new { x = 300, y = 300 } },
                objects = new[] { new { id = "desk1", kind = "desk", x = 50, y = 450, w = 150, h = 75, text = "Kassa" } },
            });
            var savedView = await saved.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("synced", savedView.GetProperty("sync").GetProperty("state").GetString());
            Assert.Equal("23", savedView.GetProperty("sync").GetProperty("skipped")[0].GetString());

            var invalid = await http.PutAsJsonAsync("/api/setup/layout", new { pcs = new Dictionary<string, object> { ["1"] = new { x = 5000, y = 0 } } });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("layout_invalid", (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetProperty("code").GetString());

            var prefs = await http.PutAsJsonAsync("/api/setup/prefs", new { lang = "ru" });
            Assert.Equal("ru", (await prefs.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("lang").GetString());
            Assert.Equal("ru", (await http.GetFromJsonAsync<JsonElement>("/api/setup/prefs")).GetProperty("lang").GetString());

            Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsync("/api/setup/close", null)).StatusCode);
            Assert.Equal(0, await host.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

[CollectionDefinition("console", DisableParallelization = true)]
public class ConsoleCollection;
