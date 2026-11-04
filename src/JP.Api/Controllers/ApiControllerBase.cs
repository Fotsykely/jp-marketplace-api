using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace JP.Api.Controllers;

/// <summary>Helpers shared by the controllers that work with the logged-in user.</summary>
public abstract class ApiControllerBase : ControllerBase
{
    // Id of the logged-in user ("sub" claim), null if missing or malformed
    protected Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;

    protected ActionResult InvalidField(string field, string message)
    {
        ModelState.AddModelError(field, message);
        return ValidationProblem(ModelState);
    }
}
