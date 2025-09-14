using Oris.HttpKit.Demo.Models;

static class Program
{
    static async Task Main()
    {
        // HttpClient setup
        var client = new HttpClient
        {
            BaseAddress = new Uri("https://jsonplaceholder.typicode.com/"),
            Timeout = TimeSpan.FromSeconds(10)
        };

        // API Client Helper
        var apiHelper = new Oris.HttpKit.Services.OrisHttpClient(client);

        // ----------------------
        // GET Example
        // ----------------------
        var users = await apiHelper.GetAsync<List<User>>("users");
        Console.WriteLine($"Fetched {users?.Count ?? 0} users.");

        // ----------------------
        // POST Example
        // ----------------------
        var newUser = new User { Name = "Narender", Email = "narender@example.com" };
        var createdUser = await apiHelper.PostAsync<User, User>("users", newUser);
        Console.WriteLine($"Created User Id: {createdUser?.Id}");

        // ----------------------
        // PUT Example
        // ----------------------
        if (createdUser != null)
        {
            createdUser.Name = "Narender Updated";
            var updatedUser = await apiHelper.PutAsync<User, User>(
                $"users/{createdUser.Id}",
                createdUser
            );
            Console.WriteLine($"Updated User Name: {updatedUser?.Name}");
        }

        // ----------------------
        // DELETE Example
        // ----------------------
        var deleteResponse = await apiHelper.DeleteAsync<string>("users/1");
        Console.WriteLine("Delete response received.");

        // ----------------------
        // PATCH Example
        // ----------------------
        var patchData = new { Name = "Narender Patched" };
        var patchedUser = await apiHelper.PatchAsync<object, User>("users/1", patchData);
        Console.WriteLine($"Patched User Name: {patchedUser?.Name}");
    }
}
