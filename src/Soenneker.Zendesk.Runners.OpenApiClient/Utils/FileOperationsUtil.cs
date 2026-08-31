using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Soenneker.Extensions.String;
using Soenneker.Git.Util.Abstract;
using Soenneker.Zendesk.Runners.OpenApiClient.Utils.Abstract;
using Soenneker.Utils.Dotnet.Abstract;
using Soenneker.Utils.Environment;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Extensions.ValueTask;
using Soenneker.Kiota.Util.Abstract;
using Soenneker.OpenApi.Fixer.Abstract;
using Soenneker.Utils.Directory.Abstract;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.File.Download.Abstract;
using Soenneker.Utils.Yaml.Abstract;
using System.Collections.Generic;

namespace Soenneker.Zendesk.Runners.OpenApiClient.Utils;

public sealed class FileOperationsUtil : IFileOperationsUtil
{
    private readonly ILogger<FileOperationsUtil> _logger;
    private readonly IConfiguration _configuration;
    private readonly IGitUtil _gitUtil;
    private readonly IDotnetUtil _dotnetUtil;
    private readonly IKiotaUtil _kiotaUtil;
    private readonly IOpenApiFixer _openApiFixer;
    private readonly IFileDownloadUtil _fileDownloadUtil;
    private readonly IFileUtil _fileUtil;
    private readonly IDirectoryUtil _directoryUtil;
    private readonly IYamlUtil _yamlUtil;

    public FileOperationsUtil(ILogger<FileOperationsUtil> logger, IConfiguration configuration, IGitUtil gitUtil, IDotnetUtil dotnetUtil,
        IFileDownloadUtil fileDownloadUtil, IFileUtil fileUtil, IDirectoryUtil directoryUtil, IKiotaUtil kiotaUtil, IOpenApiFixer openApiFixer,
        IYamlUtil yamlUtil)
    {
        _logger = logger;
        _configuration = configuration;
        _gitUtil = gitUtil;
        _dotnetUtil = dotnetUtil;
        _kiotaUtil = kiotaUtil;
        _openApiFixer = openApiFixer;
        _fileDownloadUtil = fileDownloadUtil;
        _fileUtil = fileUtil;
        _directoryUtil = directoryUtil;
        _yamlUtil = yamlUtil;
    }

    public async ValueTask Process(CancellationToken cancellationToken = default)
    {
        string gitHubToken = EnvironmentUtil.GetVariableStrict("GH__TOKEN");
        string name = EnvironmentUtil.GetVariableStrict("GIT__NAME");
        string email = EnvironmentUtil.GetVariableStrict("GIT__EMAIL");

        string gitDirectory = await _gitUtil.CloneToTempDirectory($"https://github.com/soenneker/{Constants.Library.ToLowerInvariantFast()}", cancellationToken: cancellationToken);

        string targetFilePath = Path.Combine(gitDirectory, "openapi.json");

        await _fileUtil.DeleteIfExists(targetFilePath, cancellationToken: cancellationToken);

        string openApiDocumentUrl = _configuration["Zendesk:ClientGenerationUrl"] ?? "https://developer.zendesk.com/zendesk/oas.yaml";

        string? filePath = await _fileDownloadUtil.Download(openApiDocumentUrl,
            targetFilePath, fileExtension: ".json", cancellationToken: cancellationToken);

        if (filePath == null)
            throw new InvalidOperationException("Zendesk OpenAPI document download failed.");

        string rawDocument = await _fileUtil.Read(filePath, cancellationToken: cancellationToken);
        string trimmedDocument = rawDocument.TrimStart();

        if (!trimmedDocument.StartsWith('{') && !trimmedDocument.StartsWith('['))
        {
            string convertedFilePath = Path.Combine(gitDirectory, "openapi.converted.json");
            await _fileUtil.DeleteIfExists(convertedFilePath, cancellationToken: cancellationToken);
            await _yamlUtil.SaveAsJson(filePath, convertedFilePath, cancellationToken: cancellationToken);
            filePath = convertedFilePath;
        }

        string fixedFilePath = Path.Combine(gitDirectory, "openapi.fixed.json");
        await _fileUtil.DeleteIfExists(fixedFilePath, cancellationToken: cancellationToken);
        await _openApiFixer.Fix(filePath, fixedFilePath, cancellationToken).NoSync();

        await _kiotaUtil.EnsureInstalled(cancellationToken);

        string srcDirectory = Path.Combine(gitDirectory, "src", Constants.Library);

        await DeleteAllExceptCsproj(srcDirectory, cancellationToken);

        await _kiotaUtil.Generate(fixedFilePath, "ZendeskOpenApiClient", Constants.Library, gitDirectory, cancellationToken).NoSync();

        await BuildAndPush(gitDirectory, gitHubToken, name, email, cancellationToken).NoSync();
    }

    /// <summary>
    /// Deletes generated files beneath the directory while preserving C# project files.
    /// </summary>
    /// <param name="directoryPath">Root directory whose generated contents should be removed.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes after the targeted files have been deleted.</returns>
    public async ValueTask DeleteAllExceptCsproj(string directoryPath, CancellationToken cancellationToken = default)
    {
        if (!(await _directoryUtil.Exists(directoryPath, cancellationToken)))
            throw new DirectoryNotFoundException($"Generated source directory does not exist: {directoryPath}");

        List<string> files = await _directoryUtil.GetFilesByExtension(directoryPath, "", true, cancellationToken);
        foreach (string file in files)
        {
            if (!file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                await _fileUtil.Delete(file, ignoreMissing: true, log: false, cancellationToken);
                _logger.LogInformation("Deleted file: {FilePath}", file);
            }
        }

        List<string> dirs = await _directoryUtil.GetAllDirectoriesRecursively(directoryPath, cancellationToken);
        foreach (string dir in dirs.OrderByDescending(d => d.Length))
        {
            List<string> dirFiles = await _directoryUtil.GetFilesByExtension(dir, "", false, cancellationToken);
            List<string> subDirs = await _directoryUtil.GetAllDirectories(dir, cancellationToken);
            if (dirFiles.Count == 0 && subDirs.Count == 0)
            {
                await _directoryUtil.Delete(dir, cancellationToken);
                _logger.LogInformation("Deleted empty directory: {DirectoryPath}", dir);
            }
        }
    }

    private async ValueTask BuildAndPush(string gitDirectory, string gitHubToken, string name, string email, CancellationToken cancellationToken)
    {
        string projFilePath = Path.Combine(gitDirectory, "src", Constants.Library, $"{Constants.Library}.csproj");

        await _dotnetUtil.Restore(projFilePath, cancellationToken: cancellationToken);

        bool successful = await _dotnetUtil.Build(projFilePath, true, "Release", false, cancellationToken: cancellationToken);

        if (!successful)
            throw new InvalidOperationException("The generated Zendesk OpenAPI client did not build successfully.");

        await _gitUtil.CommitAndPush(gitDirectory, "Automated update", gitHubToken, name, email, cancellationToken);
    }
}
