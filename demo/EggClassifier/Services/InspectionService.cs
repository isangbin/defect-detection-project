using EggClassifier.Models.Database;
using OpenCvSharp;

namespace EggClassifier.Services;

/// <summary>
/// Supabase 기반 검사 로그 저장 서비스
/// </summary>
public class InspectionService : IInspectionService
{
    private readonly SupabaseService _supabaseService;

    public InspectionService(SupabaseService supabaseService)
    {
        _supabaseService = supabaseService;
    }

    public async Task<bool> SaveInspectionAsync(string userId, int eggClass, double accuracy, Mat eggImage)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[SaveInspection] 시작 - UserId: {userId}, Class: {eggClass}");

            // Mat → byte[] 변환
            var imageBytes = eggImage.ToBytes(".png");
            System.Diagnostics.Debug.WriteLine($"[SaveInspection] 이미지 변환 완료 - Size: {imageBytes.Length} bytes");

            // Storage에 이미지 업로드
            var imageUrl = await UploadImageToStorageAsync(userId, imageBytes).ConfigureAwait(false);

            if (string.IsNullOrEmpty(imageUrl))
            {
                System.Diagnostics.Debug.WriteLine($"[SaveInspection] 경고: Storage 업로드 실패, URL이 null입니다.");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[SaveInspection] Storage 업로드 성공 - URL: {imageUrl}");
            }

            // DB에 저장
            var client = await _supabaseService.GetClientAsync().ConfigureAwait(false);
            var inspection = new EggEntity
            {
                UserId = userId,
                EggClass = eggClass,
                Accuracy = accuracy,
                InspectDate = DateTime.Now,
                EggImage = imageBytes,
                EggImageUrl = imageUrl
            };

            System.Diagnostics.Debug.WriteLine($"[SaveInspection] DB 저장 시도...");
            var response = await client.From<EggEntity>().Insert(inspection).ConfigureAwait(false);
            System.Diagnostics.Debug.WriteLine($"[SaveInspection] DB 저장 완료");

            return response != null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SaveInspection] 오류: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[SaveInspection] StackTrace: {ex.StackTrace}");
            return false;
        }
    }

    /// <summary>
    /// Supabase Storage "eggs" 버킷에 이미지 업로드
    /// </summary>
    private async Task<string?> UploadImageToStorageAsync(string userId, byte[] imageBytes)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[UploadStorage] 시작 - UserId: {userId}, Size: {imageBytes.Length} bytes");

            var client = await _supabaseService.GetClientAsync().ConfigureAwait(false);
            System.Diagnostics.Debug.WriteLine($"[UploadStorage] Supabase 클라이언트 획득 완료");

            // 파일명: {userId}_{timestamp}.png
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var fileName = $"{userId}_{timestamp}.png";
            System.Diagnostics.Debug.WriteLine($"[UploadStorage] 파일명 생성: {fileName}");

            // eggs 버킷에 업로드 시도
            System.Diagnostics.Debug.WriteLine($"[UploadStorage] Storage 업로드 시작...");

            var uploadResponse = await client.Storage
                .From("eggs")
                .Upload(imageBytes, fileName, new Supabase.Storage.FileOptions
                {
                    ContentType = "image/png",
                    Upsert = true
                })
                .ConfigureAwait(false);

            System.Diagnostics.Debug.WriteLine($"[UploadStorage] 업로드 응답 받음");

            // Public URL 생성
            var publicUrl = client.Storage
                .From("eggs")
                .GetPublicUrl(fileName);

            System.Diagnostics.Debug.WriteLine($"[UploadStorage] Public URL 생성: {publicUrl}");

            return publicUrl;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UploadStorage] 오류 발생!");
            System.Diagnostics.Debug.WriteLine($"[UploadStorage] Message: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[UploadStorage] Type: {ex.GetType().Name}");
            System.Diagnostics.Debug.WriteLine($"[UploadStorage] StackTrace: {ex.StackTrace}");

            if (ex.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($"[UploadStorage] InnerException: {ex.InnerException.Message}");
            }

            return null;
        }
    }

    public async Task<int> GetInspectionCountAsync(string userId)
    {
        try
        {
            var client = await _supabaseService.GetClientAsync();
            var response = await client.From<EggEntity>()
                .Where(x => x.UserId == userId)
                .Get();

            return response.Models.Count;
        }
        catch
        {
            return 0;
        }
    }

    public async Task<(int normal, int defect)> GetInspectionStatsAsync(string userId)
    {
        try
        {
            var client = await _supabaseService.GetClientAsync();
            var response = await client.From<EggEntity>()
                .Where(x => x.UserId == userId)
                .Get();

            var normal = response.Models.Count(x => x.EggClass == 0);
            var defect = response.Models.Count(x => x.EggClass > 0);

            return (normal, defect);
        }
        catch
        {
            return (0, 0);
        }
    }
}
