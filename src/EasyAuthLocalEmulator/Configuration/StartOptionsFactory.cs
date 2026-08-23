using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using EasyAuthLocalEmulator.Auth;

namespace EasyAuthLocalEmulator.Configuration;

public sealed class StartOptionsFactory
{
    private const long MaximumConfigBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public EmulatorOptions Create(StartCommandInput input)
    {
        Uri upstream = ParseUpstream(input.UpstreamUrl);
        ValidatePort(input.Port);
        EmulatedPlatform platform = ParsePlatform(input.Platform);

        EmulatorConfigurationFile configuration = LoadConfiguration(input.ConfigFile);
        ValidateSessionLifetime(configuration.SessionLifetime);
        ReadOnlyDictionary<string, UserProfile> profiles = BuildProfiles(configuration);

        if (input.NoUi && input.ConfigFile is null)
        {
            throw new InputValidationException("--no-ui requires --config.");
        }

        UserProfile? selectedProfile = null;
        string? selectedProfileName = NormalizeOptional(input.ProfileName);

        if (selectedProfileName is not null && input.ConfigFile is null)
        {
            throw new InputValidationException("--profile requires --config.");
        }

        if (selectedProfileName is not null &&
            !profiles.TryGetValue(selectedProfileName, out selectedProfile))
        {
            throw new InputValidationException(
                $"Profile '{selectedProfileName}' was not found in the configuration file.");
        }

        if (input.NoUi)
        {
            if (selectedProfile is null)
            {
                throw new InputValidationException("--no-ui requires --profile.");
            }
        }

        return new EmulatorOptions(
            upstream,
            input.Port,
            input.OpenBrowser,
            profiles,
            selectedProfileName,
            selectedProfile,
            input.NoUi,
            configuration.SessionLifetime,
            platform);
    }

    private static Uri ParseUpstream(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InputValidationException(
                "The upstream URL must be an absolute HTTP or HTTPS URL.");
        }

        if (!uri.IsLoopback)
        {
            throw new InputValidationException(
                "The upstream URL must use localhost or a loopback IP address.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InputValidationException(
                "The upstream URL cannot contain user info, a query string, or a fragment.");
        }

        UriBuilder builder = new(uri);
        if (builder.Path.Length == 0 || builder.Path[^1] != '/')
        {
            builder.Path += "/";
        }

        return builder.Uri;
    }

    private static void ValidatePort(int port)
    {
        if (port is < 1 or > 65535)
        {
            throw new InputValidationException("--port must be between 1 and 65535.");
        }
    }

    private static EmulatedPlatform ParsePlatform(string value)
    {
        if (!PlatformContracts.TryParse(value, out PlatformContract? contract))
        {
            throw new InputValidationException(
                "--platform must be either 'app-service' or 'container-apps'.");
        }

        return contract.Platform;
    }

    private static void ValidateSessionLifetime(TimeSpan lifetime)
    {
        if (lifetime < TimeSpan.FromMinutes(1) || lifetime > TimeSpan.FromDays(7))
        {
            throw new InputValidationException(
                "sessionLifetime must be between 00:01:00 and 7.00:00:00.");
        }
    }

    private static EmulatorConfigurationFile LoadConfiguration(FileInfo? file)
    {
        if (file is null)
        {
            return new EmulatorConfigurationFile();
        }

        if (!file.Exists)
        {
            throw new InputValidationException(
                $"Configuration file '{file.FullName}' does not exist.");
        }

        if (file.Length > MaximumConfigBytes)
        {
            throw new InputValidationException(
                $"Configuration file '{file.FullName}' exceeds 1 MiB.");
        }

        try
        {
            byte[] json = File.ReadAllBytes(file.FullName);
            EnsureNoDuplicateProperties(json);

            return JsonSerializer.Deserialize<EmulatorConfigurationFile>(json, SerializerOptions)
                ?? throw new InputValidationException(
                    $"Configuration file '{file.FullName}' is empty.");
        }
        catch (JsonException exception)
        {
            throw new InputValidationException(
                $"Configuration file '{file.FullName}' is invalid: {exception.Message}",
                exception);
        }
        catch (IOException exception)
        {
            throw new InputValidationException(
                $"Configuration file '{file.FullName}' could not be read: {exception.Message}",
                exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InputValidationException(
                $"Configuration file '{file.FullName}' could not be read: {exception.Message}",
                exception);
        }
    }

    private static ReadOnlyDictionary<string, UserProfile> BuildProfiles(
        EmulatorConfigurationFile configuration)
    {
        if (configuration.Profiles is null)
        {
            throw new InputValidationException("profiles cannot be null.");
        }

        Dictionary<string, UserProfile> profiles = new(StringComparer.Ordinal);

        foreach ((string name, ProfileConfiguration? profile) in configuration.Profiles)
        {
            string normalizedName = name.Trim();
            if (normalizedName.Length is < 1 or > 128)
            {
                throw new InputValidationException(
                    "Profile names must contain between 1 and 128 characters.");
            }

            if (!string.Equals(name, normalizedName, StringComparison.Ordinal))
            {
                throw new InputValidationException(
                    $"Profile name '{name}' cannot start or end with whitespace.");
            }

            if (profile is null)
            {
                throw new InputValidationException($"Profile '{name}' cannot be null.");
            }

            UserProfile validatedProfile;
            try
            {
                validatedProfile = ProfileValidator.Create(profile);
                _ = new PrincipalBuilder().Build(validatedProfile);
            }
            catch (ProfileValidationException exception)
            {
                throw new InputValidationException(
                    $"Profile '{name}' is invalid: {exception.Message}",
                    exception);
            }

            profiles.Add(name, validatedProfile);
        }

        return new ReadOnlyDictionary<string, UserProfile>(profiles);
    }

    private static string? NormalizeOptional(string? value)
    {
        if (value is null)
        {
            return null;
        }

        string normalized = value.Trim();
        if (normalized.Length == 0)
        {
            throw new InputValidationException("--profile cannot be empty.");
        }

        return normalized;
    }

    private static void EnsureNoDuplicateProperties(ReadOnlySpan<byte> json)
    {
        Utf8JsonReader reader = new(json, new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow
        });
        Stack<HashSet<string>> objectProperties = new();

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    objectProperties.Push(new HashSet<string>(StringComparer.Ordinal));
                    break;
                case JsonTokenType.PropertyName:
                    string propertyName = reader.GetString()!;
                    if (!objectProperties.Peek().Add(propertyName))
                    {
                        throw new JsonException(
                            $"Duplicate property '{propertyName}' is not allowed.");
                    }

                    break;
                case JsonTokenType.EndObject:
                    objectProperties.Pop();
                    break;
            }
        }
    }
}
