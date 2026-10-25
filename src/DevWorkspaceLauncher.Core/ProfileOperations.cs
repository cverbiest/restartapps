namespace DevWorkspaceLauncher;

/// <summary>The rules for app profiles: named sets of Kill/Restart ticks.</summary>
public static class ProfileOperations
{
    public static Profile? Find(IEnumerable<Profile> profiles, string? name) =>
        name is null
            ? null
            : profiles.FirstOrDefault(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Applies the profile's ticks. An app the profile has never seen is cleared rather than acted on,
    /// so a profile always describes the whole run. Returns how many apps were cleared that way.
    /// </summary>
    public static int Apply(Profile profile, IEnumerable<AppConfiguration> apps)
    {
        var entries = new Dictionary<string, ProfileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in profile.Entries)
        {
            entries[entry.Name] = entry;
        }

        var unknown = 0;
        foreach (var app in apps)
        {
            if (entries.TryGetValue(app.Name, out var entry))
            {
                app.Kill = entry.Kill && app.CanStop;
                app.Restart = entry.Restart;
            }
            else
            {
                app.Kill = false;
                app.Restart = false;
                unknown++;
            }
        }

        return unknown;
    }

    public static List<ProfileEntry> Capture(IEnumerable<AppConfiguration> apps) =>
        apps.Select(app => new ProfileEntry { Name = app.Name, Kill = app.Kill, Restart = app.Restart }).ToList();

    /// <summary>Replaces the entries of the profile with this name, or adds a new profile.</summary>
    public static Profile Upsert(List<Profile> profiles, string name, List<ProfileEntry> entries)
    {
        var existing = Find(profiles, name);
        if (existing is not null)
        {
            existing.Entries = entries;
            return existing;
        }

        var profile = new Profile { Name = name, Entries = entries };
        profiles.Add(profile);
        return profile;
    }
}
