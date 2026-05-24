#nullable enable
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TransparentTwitchChatWPF.Helpers;

/// <summary>
/// Manages the NativeChat overlay web files on disk.
/// NativeChat is shipped as a signed-by-build manifest plus static assets in an
/// embedded zip. The manager stages installs, verifies file hashes, activates the
/// verified folder, and keeps the previous folder as a rollback backup.
/// </summary>
public class NativeChatFileManager
{
    // Logical name set by <LogicalName> in the .csproj EmbeddedResource item.
    private const string EmbeddedZipResourceName =
        "TransparentTwitchChatWPF.Resources.native-chat.zip";

    private const string ManifestEntryName = "nativechat-manifest.json";
    private const string VersionEntryName = "version.json";
    private const int SupportedProtocolVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<NativeChatFileManager> _logger;

    public NativeChatFileManager(ILogger<NativeChatFileManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Called once on startup. Extracts the embedded zip if the overlay files are
    /// missing, corrupt, or older than the embedded version. Returns true if an
    /// install was performed.
    /// </summary>
    public bool EnsureFilesAreUpToDate()
    {
        using var package = OpenEmbeddedPackage();
        if (package == null)
        {
            _logger.LogWarning("Could not open embedded NativeChat package. Skipping extraction check.");
            return false;
        }

        if (!IsPackageCompatible(package.Manifest, out string compatibilityError))
        {
            _logger.LogError("Embedded NativeChat package is not compatible: {Reason}", compatibilityError);
            return false;
        }

        string overlayFolder = OverlayPathHelper.GetNativeChatPath();
        string installedVersion = GetInstalledVersion(overlayFolder);
        ValidationResult installedState = ValidateInstalledFiles(overlayFolder);
        string reason;

        if (!installedState.IsValid)
        {
            reason = installedState.Message;
        }
        else if (string.IsNullOrEmpty(installedVersion))
        {
            reason = "no installed version recorded";
        }
        else if (IsNewerVersion(package.Manifest.Version, installedVersion))
        {
            reason = $"embedded version {package.Manifest.Version} > installed {installedVersion}";
        }
        else
        {
            _logger.LogInformation(
                "NativeChat files are up to date (version {Version}).", installedVersion);
            return false;
        }

        _logger.LogInformation("Installing embedded NativeChat files: {Reason}.", reason);
        InstallPackage(package, overlayFolder, reason);
        SaveInstalledVersion(package.Manifest.Version);
        return true;
    }

    /// <summary>
    /// Reads the version string from the embedded NativeChat package.
    /// </summary>
    public string? GetEmbeddedVersion()
    {
        using var package = OpenEmbeddedPackage();
        return package?.Manifest.Version;
    }

    /// <summary>
    /// Forces a full re-install from the embedded zip.
    /// </summary>
    public void ForceRestoreDefaults()
    {
        using var package = OpenEmbeddedPackage() ??
            throw new InvalidOperationException("Embedded NativeChat zip resource not found in assembly.");

        if (!IsPackageCompatible(package.Manifest, out string compatibilityError))
            throw new InvalidOperationException($"Embedded NativeChat package is not compatible: {compatibilityError}");

        string overlayFolder = OverlayPathHelper.GetNativeChatPath();
        _logger.LogInformation("Force-restoring NativeChat defaults to: {Path}", overlayFolder);
        InstallPackage(package, overlayFolder, "force restore requested");
        SaveInstalledVersion(package.Manifest.Version);
    }

    /// <summary>
    /// Reads NativeChat update metadata from a zip file without installing it.
    /// This is intended for a future remote-update flow after a zip has already
    /// been downloaded by an explicit app update action.
    /// </summary>
    public NativeChatUpdateInfo InspectZipFile(string zipFilePath)
    {
        using var package = OpenZipFilePackage(zipFilePath);
        return new NativeChatUpdateInfo(
            package.Manifest.Version,
            package.Manifest.ProtocolVersion,
            package.Manifest.MinimumAppVersion,
            package.Manifest.EntryPoints.Overlay,
            package.Manifest.EntryPoints.Settings);
    }

    /// <summary>
    /// Installs a NativeChat update zip into the managed NativeChat folder.
    /// The zip must include nativechat-manifest.json and pass compatibility and
    /// hash validation. Returns false if the package is not newer and the current
    /// install is already valid.
    /// </summary>
    public bool TryInstallUpdateFromZipFile(
        string zipFilePath,
        out string statusMessage,
        bool allowDowngrade = false)
    {
        try
        {
            using var package = OpenZipFilePackage(zipFilePath);

            if (!IsPackageCompatible(package.Manifest, out string compatibilityError))
            {
                statusMessage = compatibilityError;
                return false;
            }

            string overlayFolder = OverlayPathHelper.GetNativeChatPath();
            string installedVersion = GetInstalledVersion(overlayFolder);
            ValidationResult installedState = ValidateInstalledFiles(overlayFolder);

            if (installedState.IsValid &&
                !allowDowngrade &&
                !string.IsNullOrEmpty(installedVersion) &&
                !IsNewerVersion(package.Manifest.Version, installedVersion))
            {
                statusMessage =
                    $"NativeChat update {package.Manifest.Version} is not newer than installed {installedVersion}.";
                return false;
            }

            InstallPackage(package, overlayFolder, $"external update {package.Manifest.Version}");
            SaveInstalledVersion(package.Manifest.Version);

            statusMessage = $"NativeChat {package.Manifest.Version} installed.";
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install NativeChat update package.");
            statusMessage = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Reads the version string from the root manifest or legacy version.json
    /// inside any zip stream. Returns null if metadata is absent or unparseable.
    /// </summary>
    public string? ReadVersionFromZipStream(Stream zipStream)
    {
        try
        {
            NativeChatManifest? manifest = ReadManifestFromZipStream(zipStream, logMissing: false);
            if (!string.IsNullOrEmpty(manifest?.Version))
                return manifest.Version;

            ResetStream(zipStream);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
            var entry = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName, VersionEntryName, StringComparison.OrdinalIgnoreCase));

            if (entry == null)
            {
                _logger.LogWarning("Neither {Manifest} nor {Version} found in NativeChat zip.",
                    ManifestEntryName, VersionEntryName);
                return null;
            }

            using var reader = new StreamReader(entry.Open());
            string json = reader.ReadToEnd();
            var info = JsonSerializer.Deserialize<VersionInfo>(json, JsonOptions);
            return info?.Version;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read NativeChat version metadata from zip stream.");
            return null;
        }
        finally
        {
            ResetStream(zipStream);
        }
    }

    /// <summary>
    /// Extracts and validates a manifest-backed NativeChat zip at an explicit
    /// destination. Used by update tooling; normal app installs should use
    /// TryInstallUpdateFromZipFile.
    /// </summary>
    public void ExtractFromZipFile(string zipFilePath, string destinationFolder)
    {
        using var package = OpenZipFilePackage(zipFilePath);
        InstallPackage(package, destinationFolder, "explicit zip extraction");
    }

    private NativeChatPackage? OpenEmbeddedPackage()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var stream = assembly.GetManifestResourceStream(EmbeddedZipResourceName);
        if (stream == null)
        {
            _logger.LogError(
                "Embedded resource '{Name}' not found. Available resources: {All}",
                EmbeddedZipResourceName,
                string.Join(", ", assembly.GetManifestResourceNames()));
            return null;
        }

        var manifest = ReadManifestFromZipStream(stream, logMissing: true);
        if (manifest == null)
        {
            stream.Dispose();
            _logger.LogError("Embedded NativeChat package is missing {Manifest}.", ManifestEntryName);
            return null;
        }

        ResetStream(stream);
        return new NativeChatPackage(stream, manifest, "embedded NativeChat package");
    }

    private NativeChatPackage OpenZipFilePackage(string zipFilePath)
    {
        if (!File.Exists(zipFilePath))
            throw new FileNotFoundException("NativeChat zip file not found.", zipFilePath);

        var stream = File.OpenRead(zipFilePath);
        var manifest = ReadManifestFromZipStream(stream, logMissing: true);
        if (manifest == null)
        {
            stream.Dispose();
            throw new InvalidDataException($"NativeChat update zip must include {ManifestEntryName}.");
        }

        ResetStream(stream);
        return new NativeChatPackage(stream, manifest, zipFilePath);
    }

    private NativeChatManifest? ReadManifestFromZipStream(Stream zipStream, bool logMissing)
    {
        try
        {
            ResetStream(zipStream);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
            var entry = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName, ManifestEntryName, StringComparison.OrdinalIgnoreCase));

            if (entry == null)
            {
                if (logMissing)
                    _logger.LogWarning("{Manifest} not found in NativeChat zip.", ManifestEntryName);
                return null;
            }

            using var reader = new StreamReader(entry.Open());
            string json = reader.ReadToEnd();
            var manifest = JsonSerializer.Deserialize<NativeChatManifest>(json, JsonOptions);

            if (!IsManifestShapeValid(manifest, out string error))
            {
                _logger.LogError("NativeChat manifest is invalid: {Reason}", error);
                return null;
            }

            return manifest;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read {Manifest} from NativeChat zip.", ManifestEntryName);
            return null;
        }
        finally
        {
            ResetStream(zipStream);
        }
    }

    private NativeChatManifest? ReadManifestFromFile(string manifestPath)
    {
        try
        {
            string json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<NativeChatManifest>(json, JsonOptions);
            return IsManifestShapeValid(manifest, out _) ? manifest : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read installed NativeChat manifest: {Path}", manifestPath);
            return null;
        }
    }

    private ValidationResult ValidateInstalledFiles(string destinationFolder)
    {
        if (!Directory.Exists(destinationFolder))
            return ValidationResult.Invalid("overlay directory is missing");

        string manifestPath = Path.Combine(destinationFolder, ManifestEntryName);
        if (!File.Exists(manifestPath))
            return ValidationResult.Invalid("installed manifest is missing");

        NativeChatManifest? manifest = ReadManifestFromFile(manifestPath);
        if (manifest == null)
            return ValidationResult.Invalid("installed manifest is invalid");

        if (!IsPackageCompatible(manifest, out string compatibilityError))
            return ValidationResult.Invalid(compatibilityError);

        return ValidateFolderAgainstManifest(destinationFolder, manifest, out string error)
            ? ValidationResult.Valid()
            : ValidationResult.Invalid(error);
    }

    private bool ValidateFolderAgainstManifest(
        string destinationFolder,
        NativeChatManifest manifest,
        out string error)
    {
        error = string.Empty;

        try
        {
            foreach (string entryPoint in new[]
                     {
                         manifest.EntryPoints.Settings,
                         manifest.EntryPoints.Overlay
                     })
            {
                string fullPath = GetSafeDestinationPath(destinationFolder, entryPoint);
                if (!File.Exists(fullPath))
                {
                    error = $"entrypoint is missing: {entryPoint}";
                    return false;
                }
            }

            foreach (var file in manifest.Files)
            {
                string fullPath = GetSafeDestinationPath(destinationFolder, file.Path);
                if (!File.Exists(fullPath))
                {
                    error = $"manifest file is missing: {file.Path}";
                    return false;
                }

                var info = new FileInfo(fullPath);
                if (info.Length != file.Size)
                {
                    error = $"manifest file has unexpected size: {file.Path}";
                    return false;
                }

                string actualHash = ComputeSha256(fullPath);
                if (!string.Equals(actualHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    error = $"manifest file hash mismatch: {file.Path}";
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"manifest validation failed: {ex.Message}";
            return false;
        }
    }

    private void InstallPackage(NativeChatPackage package, string destinationFolder, string reason)
    {
        string? parentFolder = Path.GetDirectoryName(destinationFolder);
        if (string.IsNullOrEmpty(parentFolder))
            throw new InvalidOperationException("NativeChat destination folder is invalid.");

        Directory.CreateDirectory(parentFolder);

        string tempFolder = Path.Combine(
            parentFolder,
            Path.GetFileName(destinationFolder) + ".installing-" + Guid.NewGuid().ToString("N"));

        try
        {
            _logger.LogInformation(
                "Staging NativeChat package {Version} from {Source}: {Reason}",
                package.Manifest.Version,
                package.SourceDescription,
                reason);

            ExtractZipStream(package.Stream, tempFolder);

            if (!ValidateFolderAgainstManifest(tempFolder, package.Manifest, out string validationError))
                throw new InvalidDataException($"Staged NativeChat package failed validation: {validationError}");

            ActivateVerifiedFolder(tempFolder, destinationFolder, package.Manifest);

            _logger.LogInformation(
                "Installed NativeChat {Version} to: {Path}",
                package.Manifest.Version,
                destinationFolder);
        }
        finally
        {
            DeleteDirectoryIfExists(tempFolder, logFailures: true);
        }
    }

    private void ExtractZipStream(Stream zipStream, string destinationFolder)
    {
        DeleteDirectoryIfExists(destinationFolder, logFailures: false);
        Directory.CreateDirectory(destinationFolder);

        ResetStream(zipStream);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            string fullPath = GetSafeDestinationPath(destinationFolder, entry.FullName);
            string? parentDir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(parentDir))
                Directory.CreateDirectory(parentDir);

            using var entryStream = entry.Open();
            using var destStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
            entryStream.CopyTo(destStream);
        }

        ResetStream(zipStream);
    }

    private void ActivateVerifiedFolder(
        string stagedFolder,
        string destinationFolder,
        NativeChatManifest manifest)
    {
        string backupFolder = destinationFolder + ".backup";
        bool movedExistingToBackup = false;

        try
        {
            DeleteDirectoryIfExists(backupFolder, logFailures: false);

            if (Directory.Exists(destinationFolder))
            {
                Directory.Move(destinationFolder, backupFolder);
                movedExistingToBackup = true;
            }

            Directory.Move(stagedFolder, destinationFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                ex,
                "Could not activate NativeChat folder by rename. Falling back to in-place file replacement.");

            if (movedExistingToBackup && !Directory.Exists(destinationFolder) && Directory.Exists(backupFolder))
                Directory.Move(backupFolder, destinationFolder);

            ReplaceContentsInPlace(stagedFolder, destinationFolder, manifest);
        }
    }

    private void ReplaceContentsInPlace(
        string stagedFolder,
        string destinationFolder,
        NativeChatManifest manifest)
    {
        Directory.CreateDirectory(destinationFolder);
        CopyDirectory(stagedFolder, destinationFolder);

        var expectedFiles = new HashSet<string>(
            Directory.EnumerateFiles(stagedFolder, "*", SearchOption.AllDirectories)
                .Select(file => NormalizeRelativePath(Path.GetRelativePath(stagedFolder, file))),
            StringComparer.OrdinalIgnoreCase);

        foreach (string file in Directory.EnumerateFiles(destinationFolder, "*", SearchOption.AllDirectories))
        {
            string relativePath = NormalizeRelativePath(Path.GetRelativePath(destinationFolder, file));
            if (expectedFiles.Contains(relativePath))
                continue;

            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not delete stale NativeChat file: {Path}", file);
            }
        }

        foreach (string directory in Directory
                     .EnumerateDirectories(destinationFolder, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not delete stale NativeChat directory: {Path}", directory);
            }
        }

        if (!ValidateFolderAgainstManifest(destinationFolder, manifest, out string validationError))
            throw new InvalidDataException($"NativeChat install failed validation: {validationError}");
    }

    private void CopyDirectory(string sourceFolder, string destinationFolder)
    {
        foreach (string sourceFile in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceFolder, sourceFile);
            string destinationFile = GetSafeDestinationPath(destinationFolder, relativePath);
            string? destinationParent = Path.GetDirectoryName(destinationFile);
            if (!string.IsNullOrEmpty(destinationParent))
                Directory.CreateDirectory(destinationParent);
            File.Copy(sourceFile, destinationFile, overwrite: true);
        }
    }

    private string GetInstalledVersion(string destinationFolder)
    {
        string manifestPath = Path.Combine(destinationFolder, ManifestEntryName);
        NativeChatManifest? manifest = File.Exists(manifestPath)
            ? ReadManifestFromFile(manifestPath)
            : null;

        return !string.IsNullOrEmpty(manifest?.Version)
            ? manifest.Version
            : App.Settings.GeneralSettings.NativeChatVersion;
    }

    private void SaveInstalledVersion(string version)
    {
        App.Settings.GeneralSettings.NativeChatVersion = version;
        App.Settings.Persist();
    }

    private bool IsPackageCompatible(NativeChatManifest manifest, out string error)
    {
        if (manifest.ProtocolVersion > SupportedProtocolVersion)
        {
            error =
                $"NativeChat protocol {manifest.ProtocolVersion} requires a newer app. Supported protocol is {SupportedProtocolVersion}.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(manifest.MinimumAppVersion) &&
            TryParseVersion(manifest.MinimumAppVersion, out Version? minimumAppVersion) &&
            TryParseVersion(AppInfo.Version, out Version? appVersion) &&
            appVersion!.CompareTo(minimumAppVersion!) < 0)
        {
            error =
                $"NativeChat {manifest.Version} requires app version {manifest.MinimumAppVersion} or newer.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private bool IsManifestShapeValid(NativeChatManifest? manifest, out string error)
    {
        if (manifest == null)
        {
            error = "manifest is empty";
            return false;
        }

        if (!string.Equals(manifest.Id, "native-chat", StringComparison.OrdinalIgnoreCase))
        {
            error = "manifest id is not native-chat";
            return false;
        }

        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            error = "manifest version is missing";
            return false;
        }

        if (manifest.ProtocolVersion <= 0)
        {
            error = "manifest protocolVersion is invalid";
            return false;
        }

        if (manifest.EntryPoints == null ||
            string.IsNullOrWhiteSpace(manifest.EntryPoints.Settings) ||
            string.IsNullOrWhiteSpace(manifest.EntryPoints.Overlay))
        {
            error = "manifest entrypoints are incomplete";
            return false;
        }

        if (manifest.Files == null || manifest.Files.Count == 0)
        {
            error = "manifest has no files";
            return false;
        }

        foreach (var file in manifest.Files)
        {
            if (string.IsNullOrWhiteSpace(file.Path) ||
                string.IsNullOrWhiteSpace(file.Sha256) ||
                file.Size < 0)
            {
                error = "manifest contains an invalid file entry";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private bool IsNewerVersion(string candidate, string current)
    {
        if (TryParseVersion(candidate, out Version? candidateVer) &&
            TryParseVersion(current, out Version? currentVer))
        {
            return candidateVer!.CompareTo(currentVer!) > 0;
        }

        _logger.LogWarning(
            "Could not parse version strings as System.Version. " +
            "Candidate='{C}', Current='{Cu}'. Falling back to string comparison.",
            candidate, current);

        return string.Compare(candidate, current, StringComparison.OrdinalIgnoreCase) > 0;
    }

    private bool TryParseVersion(string value, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string normalized = value.Split('-', '+')[0];
        return Version.TryParse(normalized, out version);
    }

    private string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private string GetSafeDestinationPath(string destinationFolder, string relativePath)
    {
        string normalizedRelativePath = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        string root = Path.GetFullPath(destinationFolder)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        string fullPath = Path.GetFullPath(Path.Combine(root, normalizedRelativePath));
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"NativeChat zip entry escapes destination folder: {relativePath}");

        return fullPath;
    }

    private string NormalizeRelativePath(string relativePath)
    {
        return relativePath
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private void DeleteDirectoryIfExists(string path, bool logFailures)
    {
        if (!Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (logFailures && ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not delete directory: {Path}", path);
        }
    }

    private void ResetStream(Stream stream)
    {
        if (stream.CanSeek)
            stream.Position = 0;
    }

    private sealed class NativeChatPackage : IDisposable
    {
        public NativeChatPackage(Stream stream, NativeChatManifest manifest, string sourceDescription)
        {
            Stream = stream;
            Manifest = manifest;
            SourceDescription = sourceDescription;
        }

        public Stream Stream { get; }
        public NativeChatManifest Manifest { get; }
        public string SourceDescription { get; }

        public void Dispose()
        {
            Stream.Dispose();
        }
    }

    private sealed class ValidationResult
    {
        private ValidationResult(bool isValid, string message)
        {
            IsValid = isValid;
            Message = message;
        }

        public bool IsValid { get; }
        public string Message { get; }

        public static ValidationResult Valid() => new(true, string.Empty);
        public static ValidationResult Invalid(string message) => new(false, message);
    }

    private sealed class NativeChatManifest
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public int ProtocolVersion { get; set; }
        public string MinimumAppVersion { get; set; } = string.Empty;
        public string GeneratedAtUtc { get; set; } = string.Empty;
        public NativeChatEntryPoints EntryPoints { get; set; } = new();
        public List<NativeChatManifestFile> Files { get; set; } = new();
    }

    private sealed class NativeChatEntryPoints
    {
        public string Settings { get; set; } = string.Empty;
        public string Overlay { get; set; } = string.Empty;
    }

    private sealed class NativeChatManifestFile
    {
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Sha256 { get; set; } = string.Empty;
    }

    private sealed record VersionInfo([property: JsonPropertyName("version")] string Version);
}

public sealed record NativeChatUpdateInfo(
    string Version,
    int ProtocolVersion,
    string MinimumAppVersion,
    string OverlayEntryPoint,
    string SettingsEntryPoint);
