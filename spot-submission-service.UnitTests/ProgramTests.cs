using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc.Testing;
using Moq;

[TestFixture]
public class ProgramTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private Mock<SpotSubmissionRepository> _repoMock = null!;

    [SetUp]
    public void SetUp()
    {
        _repoMock = new Mock<SpotSubmissionRepository>(
            Mock.Of<Amazon.DynamoDBv2.IAmazonDynamoDB>(),
            Mock.Of<IConfiguration>(),
            Mock.Of<ILogger<SpotSubmissionRepository>>());

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.AddScoped(_ => _repoMock.Object);
                });
            });
    }

    [TearDown]
    public void Teardown()
    {
        _factory.Dispose(); // Dispose to free resources
    }

    [Test]
    public async Task HealthEndpoint_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/spots/submissions/health");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var date = await response.Content.ReadFromJsonAsync<DateTime>();
        Assert.That(date, Is.Not.EqualTo(default(DateTime)));
    }
}
