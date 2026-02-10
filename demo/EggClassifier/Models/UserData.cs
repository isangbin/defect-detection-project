using System.Collections.Generic;

namespace EggClassifier.Models
{
    public class UserData
    {
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string PasswordSalt { get; set; } = string.Empty;
        public string FaceImagePath { get; set; } = string.Empty;
    }

    public class UserStore
    {
        public List<UserData> Users { get; set; } = new();
    }
}
