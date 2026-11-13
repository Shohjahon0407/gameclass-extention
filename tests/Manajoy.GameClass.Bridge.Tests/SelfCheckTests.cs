using Manajoy.GameClass.Bridge.Setup;

namespace Manajoy.GameClass.Bridge.Tests;

#pragma warning disable CA1416 // Pure mapping code; the Windows-only parts are not called here.

public class SelfCheckTests
{
    private static SelfCheckInfo Info(string copy, string download, string? detail = null) =>
        new(copy, @"C:\ProgramData\Manajoy\GameClassBridge\setup\ManajoyGameClass-Setup.exe", detail,
            download, @"C:\Users\a\Downloads\ManajoyGameClass-Setup-0.6.1.exe", detail, @"C:\x.exe", null);

    [Theory]
    [InlineData("created", CheckLevel.Ok)]
    [InlineData("in_place", CheckLevel.Ok)]
    [InlineData("running_copy", CheckLevel.Ok)]
    [InlineData("installed", CheckLevel.Info)]
    [InlineData("source_unreadable", CheckLevel.Warn)]
    [InlineData("verify_failed", CheckLevel.Warn)]
    [InlineData("failed", CheckLevel.Warn)]
    public void Copy_status_becomes_one_preflight_row(string status, CheckLevel level)
    {
        var rows = SetupCopy.Checks(Info(status, "none")).ToList();
        var row = Assert.Single(rows);
        Assert.Equal("setup_copy", row.Id);
        Assert.Equal(level, row.Level);
    }

    [Theory]
    [InlineData("unreadable", "download_unreadable", CheckLevel.Warn)]
    [InlineData("changed", "download_changed", CheckLevel.Warn)]
    [InlineData("missing", "download_missing", CheckLevel.Info)]
    [InlineData("ok", "download_file", CheckLevel.Ok)]
    public void Download_status_tells_disk_from_antivirus(string status, string id, CheckLevel level)
    {
        var row = SetupCopy.Checks(Info("running_copy", status, "0x80070017 Data error (cyclic redundancy check).")).Last();
        Assert.Equal(id, row.Id);
        Assert.Equal(level, row.Level);
    }

    [Fact]
    public void Disk_problem_is_a_warning_with_the_findings()
    {
        var info = new DiskHealthInfo(true, 3, false, ["disk 7 x2", "Ntfs 55 x1"], DateTimeOffset.Parse("2026-10-22T10:00:00+05:00"), []);
        var row = DiskHealth.ToCheck(info);
        Assert.Equal("disk_health", row.Id);
        Assert.Equal(CheckLevel.Warn, row.Level);
        Assert.Contains("disk 7 x2", row.Detail);
    }

    [Fact]
    public void Healthy_disk_is_ok()
    {
        Assert.Equal(CheckLevel.Ok, DiskHealth.ToCheck(new DiskHealthInfo(false, 0, false, [], null, [])).Level);
    }
}

public class DiskHealthLevelTests
{
#pragma warning disable CA1416
    [Fact]
    public void Retries_alone_are_listed_but_not_a_warning()
    {
        var row = DiskHealth.ToCheck(new DiskHealthInfo(false, 9, false, ["disk 153 x9"], null, []));
        Assert.Equal(CheckLevel.Ok, row.Level);
        Assert.Contains("disk 153", row.Detail);
    }
#pragma warning restore CA1416
}

public class AutoStartTests
{
    [Theory]
    [InlineData("--autostart-on", true)]
    [InlineData("--autostart-off", false)]
    public void Installer_switches_turn_the_sign_in_task_on_and_off(string arg, bool expected)
    {
        var mode = LaunchMode.Parse([arg], isWindowsService: false);
        Assert.Equal(expected, mode.SetAutoStart);
        Assert.Null(mode.Setup);
    }

    [Fact]
    public void The_sign_in_window_itself_is_a_normal_setup_launch()
    {
        var mode = LaunchMode.Parse(["--autostart"], isWindowsService: false);
        Assert.NotNull(mode.Setup);
        Assert.Null(mode.SetAutoStart);
    }

#pragma warning disable CA1416
    [Fact]
    public void Task_runs_for_any_user_with_highest_rights_after_a_delay()
    {
        var xml = AutoStart.TaskXml(@"C:\Program Files\Manajoy & Co\Manajoy.GameClass.Bridge.exe");
        Assert.Contains("<GroupId>S-1-5-32-545</GroupId>", xml);
        Assert.Contains("<RunLevel>HighestAvailable</RunLevel>", xml);
        Assert.Contains("<Delay>PT20S</Delay>", xml);
        Assert.Contains("Manajoy &amp; Co", xml);
        Assert.Contains("<Arguments>--autostart</Arguments>", xml);
    }
#pragma warning restore CA1416
}
