using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using JP.Api.Entities;
using JP.Api.Services;

namespace JP.Api.UnitTests.Services;

public class TokenServiceTests
{
    private const string Key = "unit-test-signing-key-at-least-32-bytes-long!";

    private static readonly JwtOptions Options = new()
    {
        Key = Key,
        Issuer = "jp-test",
        Audience = "jp-test-clients",
        ExpirationMinutes = 30
    };

    private static readonly User Alice = new()
    {
        Id = Guid.NewGuid(),
        UserName = "alice",
        Email = "alice@example.com"
    };

    private static TokenService NewService(JwtOptions? options = null) =>
        new(Microsoft.Extensions.Options.Options.Create(options ?? Options));

    private static JwtSecurityToken Read(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token);

    [Fact]
    public void CreateToken_ContainsUserClaims()
    {
        var (token, _) = NewService().CreateToken(Alice);

        var jwt = Read(token);
        Assert.Equal(Alice.Id.ToString(), jwt.Subject);
        Assert.Equal("alice", jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.UniqueName).Value);
        Assert.Equal("alice@example.com", jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Email).Value);
    }

    [Fact]
    public void CreateToken_UsesConfiguredIssuerAndAudience()
    {
        var (token, _) = NewService().CreateToken(Alice);

        var jwt = Read(token);
        Assert.Equal("jp-test", jwt.Issuer);
        Assert.Contains("jp-test-clients", jwt.Audiences);
    }

    [Fact]
    public void CreateToken_ExpiresAfterConfiguredMinutes()
    {
        var before = DateTime.UtcNow;

        var (token, expiresAt) = NewService().CreateToken(Alice);

        Assert.InRange(expiresAt, before.AddMinutes(30), DateTime.UtcNow.AddMinutes(30));
        // The "exp" claim only has second precision, so allow one second of difference
        Assert.InRange((Read(token).ValidTo - expiresAt).TotalSeconds, -1, 1);
    }

    [Fact]
    public void CreateToken_IsSignedWithTheConfiguredKey()
    {
        var (token, _) = NewService().CreateToken(Alice);

        var parameters = new TokenValidationParameters
        {
            ValidIssuer = "jp-test",
            ValidAudience = "jp-test-clients",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key))
        };

        // ValidateToken throws if the signature, issuer, audience or lifetime is wrong
        new JwtSecurityTokenHandler().ValidateToken(token, parameters, out var validated);
        Assert.NotNull(validated);
    }

    [Fact]
    public void CreateToken_IsRejectedWithAnotherKey()
    {
        var (token, _) = NewService().CreateToken(Alice);

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("another-key-another-key-another-key-123"))
        };

        Assert.ThrowsAny<SecurityTokenException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(token, parameters, out _));
    }
}
