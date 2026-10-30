namespace DevWorkspaceLauncher;

/// <summary>Runs an activity: plans it against what is running now, then carries the plan out.</summary>
public static class ActivityRunner
{
    public static async Task RunAsync(Activity activity, ConfigurationFile config, Action<string> log)
    {
        log($"Running activity '{activity.Name}'...");

        try
        {
            var plan = ActivityPlanner.Plan(activity, config, AppController.IsRunning, SshAliases(activity, log));
            foreach (var note in plan.Notes)
            {
                log(note);
            }

            foreach (var step in plan.Steps.Where(step => step.Kind is not (ActivityStepKind.OpenWorkspace or ActivityStepKind.ConnectRdp or ActivityStepKind.ConnectSsh)))
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

            // Opens come after the app steps, and go together so VS Code gets its pause between windows.
            var opens = plan.Steps
                .Where(step => step.Kind == ActivityStepKind.OpenWorkspace)
                .Select(step => (step.Uri!, step.Name))
                .ToList();
            if (opens.Count > 0)
            {
                await WorkspaceController.OpenAsync(opens, config.VsCodeExecutable, log);
            }

            // The connection's clean-up (temporary .rdp file and credential) runs on after the activity.
            foreach (var step in plan.Steps.Where(step => step.Kind == ActivityStepKind.ConnectRdp))
            {
                try
                {
                    RdpLauncher.Launch(RdpResolver.Resolve(config, step.RdpSession), step.Name, log);
                }
                catch (Exception ex)
                {
                    log($"Could not connect to '{step.Name}': {ex.Message}");
                }
            }

            foreach (var step in plan.Steps.Where(step => step.Kind == ActivityStepKind.ConnectSsh))
            {
                await SshLauncher.ConnectAsync(step.Name, config.SshClient, log);
            }

            log($"Activity '{activity.Name}' complete.");
        }
        catch (Exception ex)
        {
            log($"Error: {ex.Message}");
        }
    }

    /// <summary>The aliases in ~/.ssh/config, read only when the activity connects to SSH hosts.</summary>
    private static IReadOnlyCollection<string>? SshAliases(Activity activity, Action<string> log)
    {
        if (activity.SshHosts.Count == 0)
        {
            return null;
        }

        try
        {
            return SshConfigStore.Load().Document.Hosts.Select(host => host.Alias).ToList();
        }
        catch (Exception ex)
        {
            log($"Could not read {SshConfigStore.FilePath}: {ex.Message}");
            return [];
        }
    }
}
