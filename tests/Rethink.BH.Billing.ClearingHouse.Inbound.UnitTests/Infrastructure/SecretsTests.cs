using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Secrets;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.Infrastructure;

public sealed class SecretsTests : IDisposable
{
    private readonly Mock<SecretClient> _secretClient = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public void Dispose() => _cache.Dispose();

    [Theory]
    [InlineData("DefaultEndpointsProtocol=https;AccountName=x", false)]
    [InlineData("availity-hosts", true)]
    [InlineData("files.availity.com", true)]
    public void Equals_policy_treats_values_with_equals_as_literals(string value, bool isSecret)
    {
        LiteralWhenContainsEqualsPolicy.Instance.IsSecretReference(value).Should().Be(isSecret);
        LiteralWhenContainsEqualsPolicy.Instance.NormalizeSecretValue(" v ").Should().Be(" v ");
    }

    [Theory]
    [InlineData("Prd--Clearinghouse--BillingApiUrl", true)]
    [InlineData("http://localhost:5001", false)]
    [InlineData("SHA256:abc+/=", false)]
    [InlineData("has space", false)]
    public void Pattern_policy_matches_key_vault_secret_names(string value, bool isSecret)
    {
        SecretNamePatternPolicy.Instance.IsSecretReference(value).Should().Be(isSecret);
    }

    [Fact]
    public void Pattern_policy_rejects_names_longer_than_key_vault_allows_and_trims_quotes()
    {
        SecretNamePatternPolicy.Instance.IsSecretReference(new string('a', 127)).Should().BeTrue();
        SecretNamePatternPolicy.Instance.IsSecretReference(new string('a', 128)).Should().BeFalse();
        SecretNamePatternPolicy.Instance.NormalizeSecretValue("  \"https://billing\"  ").Should().Be("https://billing");
    }

    [Fact]
    public async Task Literal_values_are_returned_without_calling_key_vault()
    {
        var resolver = CreateResolver();

        var value = await resolver.ResolveAsync("UseDevelopmentStorage=true", "Blob", LiteralWhenContainsEqualsPolicy.Instance, CancellationToken.None);

        value.Should().Be("UseDevelopmentStorage=true");
        _secretClient.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Blank_values_fail_with_setting_name(string? configured)
    {
        var resolver = CreateResolver();

        var act = () => resolver.ResolveAsync(configured, "Clearinghouses:Availity:Hosts", LiteralWhenContainsEqualsPolicy.Instance, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Clearinghouses:Availity:Hosts is not configured.");
    }

    [Fact]
    public async Task Secret_names_are_read_once_and_cached()
    {
        SetupSecret("availity-hosts", "sftp.example.test");
        var resolver = CreateResolver();

        var first = await resolver.ResolveAsync("availity-hosts", "Hosts", LiteralWhenContainsEqualsPolicy.Instance, CancellationToken.None);
        var second = await resolver.ResolveAsync("availity-hosts", "Hosts", LiteralWhenContainsEqualsPolicy.Instance, CancellationToken.None);

        first.Should().Be("sftp.example.test");
        second.Should().Be(first);
        _secretClient.Verify(client => client.GetSecretAsync("availity-hosts", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Zero_cache_minutes_reads_every_time()
    {
        SetupSecret("availity-port", "22");
        var resolver = CreateResolver(cacheMinutes: 0);

        await resolver.ResolveAsync("availity-port", "Port", LiteralWhenContainsEqualsPolicy.Instance, CancellationToken.None);
        await resolver.ResolveAsync("availity-port", "Port", LiteralWhenContainsEqualsPolicy.Instance, CancellationToken.None);

        _secretClient.Verify(client => client.GetSecretAsync("availity-port", null, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Secret_values_are_normalized_by_policy()
    {
        SetupSecret("Prd--Clearinghouse--BillingApiUrl", " \"https://billing.example.test/\" ");
        var resolver = CreateResolver();

        var value = await resolver.ResolveAsync("Prd--Clearinghouse--BillingApiUrl", "BillingApiUrl", SecretNamePatternPolicy.Instance, CancellationToken.None);

        value.Should().Be("https://billing.example.test/");
    }

    [Fact]
    public async Task Empty_secret_fails_with_secret_name()
    {
        SetupSecret("empty-secret", "  ");
        var resolver = CreateResolver();

        var act = () => resolver.ResolveAsync("empty-secret", "X", SecretNamePatternPolicy.Instance, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Key Vault secret 'empty-secret' is empty.");
    }

    [Fact]
    public async Task Missing_secret_names_the_secret_and_setting()
    {
        _secretClient
            .Setup(client => client.GetSecretAsync("missing", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "SecretNotFound"));
        var resolver = CreateResolver();

        var act = () => resolver.ResolveAsync("missing", "BillingApiKey", SecretNamePatternPolicy.Instance, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Key Vault secret 'missing' was not found for BillingApiKey."))
            .WithInnerException<RequestFailedException>();
    }

    [Fact]
    public async Task Other_key_vault_errors_propagate()
    {
        _secretClient
            .Setup(client => client.GetSecretAsync("forbidden", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(403, "Forbidden"));
        var resolver = CreateResolver();

        var act = () => resolver.ResolveAsync("forbidden", "X", SecretNamePatternPolicy.Instance, CancellationToken.None);

        (await act.Should().ThrowAsync<RequestFailedException>()).Which.Status.Should().Be(403);
    }

    [Fact]
    public async Task Secret_name_without_key_vault_fails_clearly()
    {
        var resolver = new KeyVaultSecretResolver(new KeyVaultClientAccessor(null), _cache, Microsoft.Extensions.Options.Options.Create(new KeyVaultOptions()));

        var act = () => resolver.ResolveAsync("availity-hosts", "Hosts", LiteralWhenContainsEqualsPolicy.Instance, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Hosts 'availity-hosts' is a Key Vault secret name, but KeyVaultUri is not configured.");
    }

    [Fact]
    public async Task Resolver_guards_arguments()
    {
        var resolver = CreateResolver();

        await FluentActions.Invoking(() => resolver.ResolveAsync("x", "X", null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        FluentActions.Invoking(() => new KeyVaultSecretResolver(null!, _cache, Microsoft.Extensions.Options.Options.Create(new KeyVaultOptions()))).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new KeyVaultSecretResolver(new KeyVaultClientAccessor(null), _cache, null!)).Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("rta-billing-prd-kv", "https://other.vault.azure.net/", "https://rta-billing-prd-kv.vault.azure.net/")]
    [InlineData("NotConfigured", "https://bai-ch-eus2-dev-kv.vault.azure.net/", "https://bai-ch-eus2-dev-kv.vault.azure.net/")]
    [InlineData(null, "https://bai-ch-eus2-dev-kv.vault.azure.net/", "https://bai-ch-eus2-dev-kv.vault.azure.net/")]
    [InlineData(null, "not a uri", null)]
    [InlineData(null, null, null)]
    public void Vault_uri_prefers_key_vault_name_like_the_legacy_factory(string? vaultName, string? vaultUri, string? expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["KEY_VAULT_NAME"] = vaultName, ["KeyVaultUri"] = vaultUri })
            .Build();

        KeyVaultClientAccessor.ResolveVaultUri(configuration)?.ToString().Should().Be(expected);
        if (expected is null)
        {
            KeyVaultClientAccessor.ResolveVaultUri(configuration).Should().BeNull();
            KeyVaultClientAccessor.FromConfiguration(configuration).Client.Should().BeNull();
        }
        else
        {
            KeyVaultClientAccessor.FromConfiguration(configuration).Client!.VaultUri.Should().Be(new Uri(expected));
        }
    }

    [Fact]
    public void Vault_uri_requires_configuration()
    {
        FluentActions.Invoking(() => KeyVaultClientAccessor.ResolveVaultUri(null!)).Should().Throw<ArgumentNullException>();
        new KeyVaultOptions().SecretCacheMinutes.Should().Be(15);
    }

    private KeyVaultSecretResolver CreateResolver(int cacheMinutes = 15)
        => new(
            new KeyVaultClientAccessor(_secretClient.Object),
            _cache,
            Microsoft.Extensions.Options.Options.Create(new KeyVaultOptions { SecretCacheMinutes = cacheMinutes }));

    private void SetupSecret(string name, string value)
        => _secretClient
            .Setup(client => client.GetSecretAsync(name, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(SecretModelFactory.KeyVaultSecret(new SecretProperties(name), value), Mock.Of<Response>()));
}
