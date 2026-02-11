using EggClassifier.Models;
using EggClassifier.Models.Database;
using OpenCvSharp;
using System.Security.Cryptography;
using System.Text;

namespace EggClassifier.Services;

/// <summary>
/// Supabase 기반 사용자 관리 서비스
/// </summary>
public class SupabaseUserService : IUserService
{
    private readonly SupabaseService _supabaseService;
    private readonly IFaceService _faceService;

    public SupabaseUserService(SupabaseService supabaseService, IFaceService faceService)
    {
        _supabaseService = supabaseService;
        _faceService = faceService;
    }

    public bool UserExists(string username)
    {
        try
        {
            var client = _supabaseService.GetClientAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            var response = client.From<UserEntity>()
                .Where(x => x.UserId == username)
                .Get()
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();

            return response.Models.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    public bool RegisterUser(string username, string password, string faceImagePath, string role = "USER")
    {
        try
        {
            // 1. 중복 체크
            if (UserExists(username))
                return false;

            // 2. 얼굴 이미지에서 임베딩 추출
            using var faceImage = Cv2.ImRead(faceImagePath);
            if (faceImage.Empty())
                return false;

            var faceEmbedding = _faceService.GetFaceEmbedding(faceImage);
            if (faceEmbedding == null || faceEmbedding.Length == 0)
                return false;

            // 3. 비밀번호 해싱
            var salt = GenerateSalt();
            var hash = HashPassword(password, salt);

            // 4. DB에 저장 (비밀번호는 hash + salt를 합쳐서 저장)
            var client = _supabaseService.GetClientAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            var user = new UserEntity
            {
                UserId = username,
                UserPassword = $"{hash}:{salt}",  // "hash:salt" 형식
                UserName = username,
                UserFace = faceEmbedding,
                UserRole = role
            };

            var response = client.From<UserEntity>()
                .Insert(user)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();

            return response != null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RegisterUser 오류: {ex.Message}");
            return false;
        }
    }

    public UserData? ValidateCredentials(string username, string password)
    {
        try
        {
            // 1. DB에서 사용자 조회
            var client = _supabaseService.GetClientAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            var response = client.From<UserEntity>()
                .Where(x => x.UserId == username)
                .Single()
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();

            if (response == null)
                return null;

            // 2. 비밀번호 검증
            var parts = response.UserPassword.Split(':');
            if (parts.Length != 2)
                return null;

            var storedHash = parts[0];
            var storedSalt = parts[1];

            var inputHash = HashPassword(password, storedSalt);
            if (inputHash != storedHash)
                return null;

            // 3. UserData 반환 (FaceEmbedding 포함)
            return new UserData
            {
                Username = response.UserId,
                PasswordHash = storedHash,
                PasswordSalt = storedSalt,
                FaceImagePath = string.Empty,  // Supabase에서는 사용 안 함
                FaceEmbedding = response.UserFace
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ValidateCredentials 오류: {ex.Message}");
            return null;
        }
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
}
