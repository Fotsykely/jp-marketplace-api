using System.ComponentModel.DataAnnotations;

namespace JP.Api.Dtos;

public record RegisterRequest(
    [Required, StringLength(50, MinimumLength = 3)] string UserName,
    [Required, EmailAddress, StringLength(255)] string Email,
    [Required] string Password,
    [StringLength(100)] string? City);

/// <summary>Login accepts either the user name or the email.</summary>
public record LoginRequest(
    [Required] string Login,
    [Required] string Password);

public record AuthResponse(string AccessToken, DateTime ExpiresAt, UserDto User);

/// <summary>What we return about an account. Never the password or its hash.</summary>
public record UserDto(Guid Id, string UserName, string Email, string? City);
