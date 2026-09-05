using CaneFactory.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace CaneFactory.API.Controllers;

[ApiController]
[Route("api/license")]
public class LicenseController : ControllerBase
{
    private readonly LicenseService _license;
    public LicenseController(LicenseService license) => _license = license;

    [HttpGet("status")]
    public async Task<IActionResult> Status() => Ok(await _license.CheckAsync());
}
