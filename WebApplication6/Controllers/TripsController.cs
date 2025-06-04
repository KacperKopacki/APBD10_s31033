using Microsoft.AspNetCore.Mvc;
using WebApplication6.DTO;
using WebApplication6.Exception;
using WebApplication6.Service;

namespace WebApplication6.Controllers;


[ApiController]
[Route("api/[controller]")]
public class TripsController(IDbService dbservice) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetTrips([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        try
        {
            var result = await dbservice.GetTripsAsync(page, pageSize);
            return Ok(result);
        }
        catch (System.Exception e)
        {
            return BadRequest(e.Message);
        }
    }
    
    [HttpPost("{idTrip}/clients")]
    public async Task<IActionResult> AssignClientToTrip(int idTrip, [FromBody] AssignClientToTripRequest request)
    {
        try
        {
            await dbservice.AssignClientToTripAsync(idTrip, request);
            return Ok("Client assigned to trip successfully.");
        }
        catch (NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (System.Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }
}