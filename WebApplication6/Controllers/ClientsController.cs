using Microsoft.AspNetCore.Mvc;
using WebApplication6.Exception;
using WebApplication6.Service;

namespace WebApplication6.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClientsController(IDbService dbService) : ControllerBase
{
    [HttpDelete("{idClient}")]
    public async Task<IActionResult> DeleteClient(int idClient)
    {
        try
        {
            await dbService.DeleteClientAsync(idClient);
            return Ok($"Client with ID {idClient} has been deleted.");
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message); 
        }
        catch (NotFoundException ex)
        {
            return NotFound(ex.Message); 
        }
        catch (System.Exception ex)
        {
            return StatusCode(500, $"Unexpected error: {ex.Message}");
        }
    }
}