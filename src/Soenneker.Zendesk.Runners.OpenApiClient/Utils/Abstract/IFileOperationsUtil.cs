using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Zendesk.Runners.OpenApiClient.Utils.Abstract;

/// <summary>
/// Regenerates, validates, and publishes the Zendesk OpenAPI client.
/// </summary>
public interface IFileOperationsUtil
{
    /// <summary>
    /// Runs the client regeneration and publishing workflow.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the full processing workflow has finished.</returns>
    ValueTask Process(CancellationToken cancellationToken = default);
}
