using System.Text.Json;

namespace DevWorkspaceLauncher.Tests;

public class ActionsTests
{
    private const string MoveWindows = "Move all windows to the main display";

    [Fact]
    public void A_file_from_before_actions_is_converted_once()
    {
        var configuration = AppConfigurationStore.Parse("""
            {
              "Apps": [ { "Name": "Teams", "Commandline": "teams.exe", "Favorite": true } ],
              "Activities": [ { "Name": "Work", "Apps": [ { "Name": "Teams", "Action": "Restart" } ] } ]
            }
            """);

        Assert.True(ActionMigration.IsNeeded(configuration));
        ActionMigration.Convert(configuration);

        Assert.False(ActionMigration.IsNeeded(configuration));
        var teams = Assert.Single(configuration.Applications);
        Assert.Equal(("Teams", true), (teams.Name, teams.Favorite));
        var item = Assert.Single(configuration.Activities[0].Actions);
        Assert.Equal(("Teams", ActionVerb.Restart), (item.Name, item.Action));

        var json = AppConfigurationStore.ToJson(configuration);
        Assert.DoesNotContain("\"Apps\"", json);
        Assert.Contains("\"Kind\": \"Application\"", json);
    }

    [Fact]
    public void Kind_is_read_in_any_case_and_an_entry_without_one_is_an_application()
    {
        var configuration = AppConfigurationStore.Parse("""
            { "Actions": [
                { "Name": "Tool", "Commandline": "tool.exe" },
                { "kind": "builtin", "BuiltIn": "movewindowstomaindisplay", "Name": "Move all windows to the main display", "Favorite": true }
            ] }
            """);

        Assert.Equal("tool.exe", Assert.Single(configuration.Applications).Commandline);
        var builtIn = Assert.Single(configuration.Actions.OfType<BuiltInAction>());
        Assert.Equal((BuiltInActionId.MoveWindowsToMainDisplay, true), (builtIn.BuiltIn, builtIn.Favorite));
    }

    [Fact]
    public void An_unknown_kind_names_the_valid_ones()
    {
        var error = Assert.Throws<JsonException>(() => AppConfigurationStore.Parse("""{ "Actions": [ { "Kind": "Script", "Name": "X" } ] }"""));

        Assert.Contains("Unknown Kind 'Script'. Use Application or BuiltIn.", error.Message);
    }

    [Fact]
    public void Every_built_in_action_is_on_offer_but_only_written_while_pinned_or_used()
    {
        var configuration = AppConfigurationStore.Parse("{}");
        var builtIn = Assert.Single(configuration.Actions.OfType<BuiltInAction>());
        Assert.Equal(MoveWindows, builtIn.Name);
        Assert.DoesNotContain("BuiltIn", AppConfigurationStore.ToJson(configuration));

        // Writing leaves it in memory.
        Assert.Same(builtIn, Assert.Single(configuration.Actions));

        builtIn.Favorite = true;
        Assert.Contains("\"Kind\": \"BuiltIn\"", AppConfigurationStore.ToJson(configuration));

        builtIn.Favorite = false;
        configuration.Activities.Add(new Activity { Name = "Docked", Actions = [new ActivityAction { Name = MoveWindows, Action = ActionVerb.Run }] });
        var reloaded = AppConfigurationStore.Parse(AppConfigurationStore.ToJson(configuration));
        Assert.Single(reloaded.Actions.OfType<BuiltInAction>());
    }

    [Fact]
    public void A_built_in_action_is_added_to_an_activity_as_Run()
    {
        var activity = new Activity { Name = "Docked" };
        var builtIn = new BuiltInAction { Name = MoveWindows, BuiltIn = BuiltInActionId.MoveWindowsToMainDisplay };

        Assert.True(ActivityOperations.AddAction(activity, builtIn));

        Assert.Equal(ActionVerb.Run, Assert.Single(activity.Actions).Action);
    }

    [Fact]
    public void Built_in_actions_run_last_and_only_with_a_verb_they_have()
    {
        var configuration = AppConfigurationStore.Parse("""{ "Actions": [ { "Name": "A", "Commandline": "a.exe" } ] }""");
        var activity = new Activity
        {
            Name = "Docked",
            Actions =
            [
                new ActivityAction { Name = MoveWindows, Action = ActionVerb.Run },
                new ActivityAction { Name = "A", Action = ActionVerb.Start },
                new ActivityAction { Name = MoveWindows, Action = ActionVerb.Stop }
            ],
            RdpSessions = [new ActivityRdpSession { Name = "demo" }]
        };
        configuration.RdpSessions.Add(new RdpSession { Name = "demo", Host = "demo25" });

        var plan = ActivityPlanner.Plan(activity, configuration, _ => false);

        Assert.Equal(
            [ActivityStepKind.StartApp, ActivityStepKind.ConnectRdp, ActivityStepKind.RunBuiltIn],
            plan.Steps.Select(step => step.Kind));
        Assert.Contains($"{MoveWindows} cannot Stop; skipped.", plan.Notes);
    }

    [Fact]
    public void A_window_keeps_its_offset_and_size_on_the_main_display()
    {
        var moved = WindowMover.Map(new PixelRect(-2460, 100, -1460, 900), new PixelRect(-2560, 0, 0, 1400), new PixelRect(0, 0, 1920, 1040));

        Assert.Equal(new PixelRect(100, 100, 1100, 900), moved);
    }

    [Fact]
    public void A_window_that_would_stick_out_is_pulled_in_and_shrunk_to_fit()
    {
        var moved = WindowMover.Map(new PixelRect(-1000, 300, 0, 1500), new PixelRect(-2560, 0, 0, 1400), new PixelRect(0, 0, 1920, 1040));

        Assert.Equal(new PixelRect(920, 0, 1920, 1040), moved);
    }
}
