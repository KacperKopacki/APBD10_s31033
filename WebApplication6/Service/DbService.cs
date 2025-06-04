using System.Globalization;
using Microsoft.Data.SqlClient;
using WebApplication6.DTO;
using WebApplication6.Exception;

namespace WebApplication6.Service;

public interface IDbService
{
    Task<TripListResponseDto> GetTripsAsync(int page, int pageSize);
    Task DeleteClientAsync(int idClient);
    Task AssignClientToTripAsync(int idTrip, AssignClientToTripRequest request);
}

public class DbService(IConfiguration config) : IDbService
{
    public async Task<TripListResponseDto> GetTripsAsync(int page, int pageSize)
    {
        var connectionString = config.GetConnectionString("Default");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        
        var countCmd = new SqlCommand("SELECT COUNT(*) FROM Trip", connection);
        var totalCount = (int)await countCmd.ExecuteScalarAsync();
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
        int offset = (page - 1) * pageSize;
        
        var tripsCmd = new SqlCommand(@"
        SELECT t.IdTrip, t.Name, t.Description, t.DateFrom, t.DateTo, t.MaxPeople,
               c.Name AS CountryName,
               cl.FirstName AS ClientFirstName, cl.LastName AS ClientLastName
        FROM Trip t
        LEFT JOIN Country_Trip ct ON ct.IdTrip = t.IdTrip
        LEFT JOIN Country c ON c.IdCountry = ct.IdCountry
        LEFT JOIN Client_Trip clt ON clt.IdTrip = t.IdTrip
        LEFT JOIN Client cl ON cl.IdClient = clt.IdClient
        ORDER BY t.DateFrom DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
    ", connection);
        tripsCmd.Parameters.AddWithValue("@Offset", offset);
        tripsCmd.Parameters.AddWithValue("@PageSize", pageSize);
        
        var reader = await tripsCmd.ExecuteReaderAsync();

        var tripDict = new Dictionary<int, TripDto>();
        
        while (await reader.ReadAsync())
        {
            int idTrip = reader.GetInt32(0);
            if (!tripDict.TryGetValue(idTrip, out var trip))
            {
                trip = new TripDto
                {
                    Name = reader.GetString(1),
                    Description = reader.GetString(2),
                    DateFrom = reader.IsDBNull(3) ? null : reader.GetDateTime(3).ToString("yyyy-MM-dd"),
                    DateTo = reader.IsDBNull(4) ? null : reader.GetDateTime(4).ToString("yyyy-MM-dd"),
                    MaxPeople = reader.GetInt32(5),
                };
                tripDict[idTrip] = trip;
            }

            if (!reader.IsDBNull(6))
            {
                trip.Countries.Add(new CountryDto { Name = reader.GetString(6) });
            }

            if (!reader.IsDBNull(7) && !reader.IsDBNull(8))
            {
                trip.Clients.Add(new ClientDto
                {
                    FirstName = reader.GetString(7),
                    LastName = reader.GetString(8)
                });
            }
        }

        return new TripListResponseDto
        {
            PageNum = page,
            PageSize = pageSize,
            AllPages = totalPages,
            Trips = tripDict.Values.ToList()
        };
    }

    public async Task DeleteClientAsync(int idClient)
    {
        var connectionString = config.GetConnectionString("Default");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        
        var checkCmd = new SqlCommand("SELECT COUNT(*) FROM Client WHERE IdClient = @IdClient", connection);
        checkCmd.Parameters.AddWithValue("@IdClient", idClient);
        
        
        var exists = (int)await checkCmd.ExecuteScalarAsync() > 0;
        if (!exists)
            throw new NotFoundException($"Client with ID {idClient} does not exist.");
        
        var tripsCmd = new SqlCommand("SELECT COUNT(*) FROM Client_Trip WHERE IdClient = @IdClient", connection);
        tripsCmd.Parameters.AddWithValue("@IdClient", idClient);
        
        var tripCount = (int)await tripsCmd.ExecuteScalarAsync();
        if (tripCount > 0)
            throw new InvalidOperationException("Cannot delete client — client is assigned to at least one trip.");
        
        var deleteCmd = new SqlCommand("DELETE FROM Client WHERE IdClient = @IdClient", connection);
        deleteCmd.Parameters.AddWithValue("@IdClient", idClient);
        await deleteCmd.ExecuteNonQueryAsync();
    }

    public async Task AssignClientToTripAsync(int idTrip, AssignClientToTripRequest request)
    {
        if (request.IdTrip != idTrip)
            throw new InvalidOperationException("IdTrip in URL and body do not match.");

        var connectionString = config.GetConnectionString("Default");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();

        try
        {
            var tripCmd = new SqlCommand("SELECT Name, DateFrom FROM Trip WHERE IdTrip = @IdTrip", connection,
                transaction);
            tripCmd.Parameters.AddWithValue("@IdTrip", idTrip);
            var reader = await tripCmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                throw new NotFoundException("Trip not found.");

            var dbName = reader.GetString(0);
            var dateFrom = reader.GetDateTime(1);
            reader.Close();

            if (!string.Equals(dbName, request.TripName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Trip name does not match.");

            if (dateFrom < DateTime.Now.Date)
                throw new InvalidOperationException("Cannot assign to a past trip.");

            int clientId;
            var checkClientCmd =
                new SqlCommand("SELECT IdClient FROM Client WHERE Pesel = @Pesel", connection, transaction);
            checkClientCmd.Parameters.AddWithValue("@Pesel", request.Pesel);
            var result = await checkClientCmd.ExecuteScalarAsync();

            if (result == null)
            {
                var insertClientCmd = new SqlCommand(@"
                INSERT INTO Client (FirstName, LastName, Email, Telephone, Pesel)
                OUTPUT INSERTED.IdClient
                VALUES (@FirstName, @LastName, @Email, @Telephone, @Pesel)", connection, transaction);

                insertClientCmd.Parameters.AddWithValue("@FirstName", request.FirstName);
                insertClientCmd.Parameters.AddWithValue("@LastName", request.LastName);
                insertClientCmd.Parameters.AddWithValue("@Email", request.Email);
                insertClientCmd.Parameters.AddWithValue("@Telephone", request.Telephone);
                insertClientCmd.Parameters.AddWithValue("@Pesel", request.Pesel);

                clientId = (int)await insertClientCmd.ExecuteScalarAsync();
            }
            else
            {
                clientId = (int)result;
            }

            var existsCmd =
                new SqlCommand("SELECT COUNT(*) FROM Client_Trip WHERE IdClient = @IdClient AND IdTrip = @IdTrip",
                    connection, transaction);
            existsCmd.Parameters.AddWithValue("@IdClient", clientId);
            existsCmd.Parameters.AddWithValue("@IdTrip", idTrip);
            if ((int)await existsCmd.ExecuteScalarAsync() > 0)
                throw new InvalidOperationException("Client already assigned to this trip.");

            var insertCmd = new SqlCommand(@"
            INSERT INTO Client_Trip (IdClient, IdTrip, RegisteredAt, PaymentDate)
            VALUES (@IdClient, @IdTrip, @RegisteredAt, @PaymentDate)", connection, transaction);

            insertCmd.Parameters.AddWithValue("@IdClient", clientId);
            insertCmd.Parameters.AddWithValue("@IdTrip", idTrip);
            var now = DateTime.Now;
            int registeredAtInt = int.Parse(now.ToString("yyyyMMdd"));
            insertCmd.Parameters.AddWithValue("@RegisteredAt", registeredAtInt);

            if (string.IsNullOrWhiteSpace(request.PaymentDate))
            {
                insertCmd.Parameters.AddWithValue("@PaymentDate", DBNull.Value);
            }
            else
            {
                var formats = new[] { "yyyy-MM-dd", "yyyyMMdd", "MM/dd/yyyy", "dd.MM.yyyy" };
                if (!DateTime.TryParseExact(request.PaymentDate, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                {
                    throw new InvalidOperationException("Invalid date format. Use yyyy-MM-dd, yyyyMMdd, etc.");
                }

                int paymentDateInt = int.Parse(parsedDate.ToString("yyyyMMdd"));
                insertCmd.Parameters.AddWithValue("@PaymentDate", paymentDateInt);
            }
        }
        catch
        {
            await transaction.RollbackAsync();
            throw; 
        }
    }
}