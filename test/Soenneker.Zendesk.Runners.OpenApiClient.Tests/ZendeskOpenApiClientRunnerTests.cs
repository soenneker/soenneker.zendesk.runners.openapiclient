using Soenneker.Tests.HostedUnit;

namespace Soenneker.Zendesk.Runners.OpenApiClient.Tests;

[ClassDataSource<Host>(Shared = SharedType.PerTestSession)]
public sealed class ZendeskOpenApiClientRunnerTests : HostedUnitTest
{
    public ZendeskOpenApiClientRunnerTests(Host host) : base(host)
    {
    }

    [Test]
    public void Default()
    {

    }
}
