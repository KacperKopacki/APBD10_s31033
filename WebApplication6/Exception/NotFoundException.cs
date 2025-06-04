namespace WebApplication6.Exception;

public class NotFoundException : System.Exception
{
    public NotFoundException(string message) : base(message)
    {
    }
}