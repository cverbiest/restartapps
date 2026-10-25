namespace DevWorkspaceLauncher;

/// <summary>Runs an activity: plans it against what is running now, then carries the plan out.</summary>
public static class ActivityRunner
{
    public static async Task RunAsync(Activity activity, ConfigurationFile config, Action<string> log)
    {
        log($"Running activity '{activity.Name}'...");

        var plan = ActivityPlanner.Plan(activity, config, AppController.IsRunning);
        foreach (var note in plan.Notes)
        {
            log(note);
        }

        try
        {
            foreach (var step in plan.Steps.Where(step => step.Kind != ActivityStepKind.OpenWorkspace))
            {
                switch (step.Kind)
                {
                    case ActivityStepKind.StopApp:
                        await AppController.StopAsync(step.App!, log);
                        break;

                    case ActivityStepKind.CloseWorkspace:
                        await WorkspaceController.CloseAsync(step.Name, step.WindowTitle, log);
                        break;

                    case ActivityStepKind.Wait:
                        await Task.Delay(TimeSpan.FromSeconds(1));
                        break;

                    case ActivityStepKind.StartApp:
                        AppController.Start(step.App!, log);
                        break;
                }
            }

            // Opens come last in a plan, and go together so VS Code gets its pause between windows.
            var opens = plan.Steps
                .Where(step => step.Kind == ActivityStepKind.OpenWorkspace)
                .Select(step => (step.Uri!, step.Name))
                .ToList();
            if (opens.Count > 0)
            {
                await WorkspaceController.OpenAsync(opens, config.VsCodeExecutable, log);
            }

            log($"Activity '{activity.Name}' complete.");
        }
        catch (Exception ex)
        {
            log($"Error: {ex.Message}");
        }
    }
}
