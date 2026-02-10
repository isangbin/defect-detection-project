using EggClassifier.Models;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EggClassifier.Services
{
    public class UserService : IUserService
    {
        private readonly string _dataDir;
        private readonly string _dataFile;
        private UserStore _store;

        public UserService()
        {
            _dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "userdata");
            _dataFile = Path.Combine(_dataDir, "users.json");
            _store = LoadStore();
        }

        public bool UserExists(string username)
        {
            return _store.Users.Any(u =>
                string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
        }

        public bool RegisterUser(string username, string password, string faceImagePath)
        {
            if (UserExists(username))
                return false;

            string salt = GenerateSalt();
            string hash = HashPassword(password, salt);

            var user = new UserData
            {
                Username = username,
                PasswordHash = hash,
                PasswordSalt = salt,
                FaceImagePath = faceImagePath
            };

            _store.Users.Add(user);
            SaveStore();
            return true;
        }

        public UserData? ValidateCredentials(string username, string password)
        {
            var user = _store.Users.FirstOrDefault(u =>
                string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));

            if (user == null)
                return null;

            string hash = HashPassword(password, user.PasswordSalt);
            return hash == user.PasswordHash ? user : null;
        }

        private static string GenerateSalt()
        {
            var bytes = new byte[16];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }

        private static string HashPassword(string password, string salt)
        {
            using var sha256 = SHA256.Create();
            var combined = Encoding.UTF8.GetBytes(password + salt);
            var hash = sha256.ComputeHash(combined);
            return Convert.ToBase64String(hash);
        }

        private UserStore LoadStore()
        {
            if (!File.Exists(_dataFile))
                return new UserStore();

            try
            {
                var json = File.ReadAllText(_dataFile);
                return JsonSerializer.Deserialize<UserStore>(json) ?? new UserStore();
            }
            catch
            {
                return new UserStore();
            }
        }

        private void SaveStore()
        {
            Directory.CreateDirectory(_dataDir);
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_store, options);
            File.WriteAllText(_dataFile, json);
        }
    }
}
