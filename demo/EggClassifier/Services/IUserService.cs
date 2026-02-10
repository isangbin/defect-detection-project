using EggClassifier.Models;

namespace EggClassifier.Services
{
    public interface IUserService
    {
        bool UserExists(string username);
        bool RegisterUser(string username, string password, string faceImagePath);
        UserData? ValidateCredentials(string username, string password);
    }
}
