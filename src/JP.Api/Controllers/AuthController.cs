using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using JP.Api.Dtos;
using JP.Api.Entities;

namespace JP.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(UserManager<User> userManager) : ControllerBase
{
    // POST /api/auth/register
    [HttpPost("register")]
    public async Task<ActionResult<UserDto>> Register(RegisterRequest req)
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

        var dto = new UserDto(user.Id, user.UserName!, user.Email!, user.City);
        return StatusCode(StatusCodes.Status201Created, dto);
    }
}
