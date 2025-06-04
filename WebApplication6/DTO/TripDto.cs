namespace WebApplication6.DTO;

public class TripDto
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string? DateFrom { get; set; }
    public string? DateTo { get; set; }
    public int MaxPeople { get; set; }
    public List<CountryDto> Countries { get; set; } = new();
    public List<ClientDto> Clients { get; set; } = new();
}