using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using JP.Api.Dtos;
using JP.Api.Entities;
using JP.Api.Services;

namespace JP.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(UserManager<User> userManager, TokenService tokenService) : ControllerBase
{
    // POST /api/auth/register
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest req)
    {
        var user = new User
        {
            UserName = req.UserName.Trim(),
            Email = req.Email.Trim(),
            City = req.City
        };

        // Hashes the password, checks the rules (length, unique email...) and saves
        var result = await userManager.CreateAsync(user, req.Password);

        if (!result.Succeeded)
        {
            // Turn Identity's errors into a standard 400 (same shape as validation errors)
            foreach (var error in result.Errors)
                ModelState.AddModelError(error.Code, error.Description);
            return ValidationProblem(ModelState);
        }

        return StatusCode(StatusCodes.Status201Created, BuildAuthResponse(user));
    }

    // POST /api/auth/login
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest req)
    {
        var login = req.Login.Trim();
        var user = login.Contains('@')
            ? await userManager.FindByEmailAsync(login)
            : await userManager.FindByNameAsync(login);

        // Same answer for "unknown user" and "wrong password": don't reveal which accounts exist
        if (user is null || !await userManager.CheckPasswordAsync(user, req.Password))
            return Unauthorized(new ProblemDetails { Title = "Identifiants invalides." });

        return BuildAuthResponse(user);
    }

    // GET /api/auth/me  (requires a valid token)
    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me()
    {
        // "sub" is the user id we put in the token (see TokenService)
        var id = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var user = id is null ? null : await userManager.FindByIdAsync(id);
        return user is null ? Unauthorized() : ToDto(user);
    }

    // Shared by register and login: a fresh token plus the account details
    private AuthResponse BuildAuthResponse(User user)
    {
        var (token, expiresAt) = tokenService.CreateToken(user);
        return new AuthResponse(token, expiresAt, ToDto(user));
    }

    private static UserDto ToDto(User u) => new(u.Id, u.UserName!, u.Email!, u.City);
}
