using EggClassifier.Models.Database;
using OpenCvSharp;
using Npgsql;

namespace EggClassifier.Services;

/// <summary>
/// Supabase 기반 검사 로그 저장 서비스
/// </summary>
public class InspectionService : IInspectionService
{
    private readonly SupabaseService _supabaseService;
    private readonly string _connString = "Host=aws-1-ap-northeast-2.pooler.supabase.com;Port=6543;Database=postgres;Username=postgres.vvsdcdlazsqkwmaevmvq;Password=dlatkdqls1264";

    public InspectionService(SupabaseService supabaseService)
    {
        _supabaseService = supabaseService;
    }

    public async Task<bool> SaveInspectionAsync(string userId, int eggClass, double accuracy, Mat eggImage)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[SaveInspection] 시작 - UserId: {userId}, Class: {eggClass}");

            // Mat → byte[] 변환 (JPEG 형식)
            var success = Cv2.ImEncode(".jpg", eggImage, out var imageBytes);
            if (!success || imageBytes == null || imageBytes.Length == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveInspection] 이미지 인코딩 실패");
                return false;
            }
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

            // DB에 저장 (Npgsql 직접 사용 - bytea에 바이너리 그대로 저장)
            System.Diagnostics.Debug.WriteLine($"[SaveInspection] DB 저장 시도...");

            using (var conn = new NpgsqlConnection(_connString))
            {
                await conn.OpenAsync().ConfigureAwait(false);

                string sql = @"
                    INSERT INTO egg (user_id, egg_class, accuracy, inspect_date, egg_image, egg_image_url)
                    VALUES (@user_id, @egg_class, @accuracy, @inspect_date, @egg_image, @egg_image_url)";

                using (var cmd = new NpgsqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("user_id", userId);
                    cmd.Parameters.AddWithValue("egg_class", eggClass);
                    cmd.Parameters.AddWithValue("accuracy", accuracy);
                    cmd.Parameters.AddWithValue("inspect_date", DateTime.Now);
                    cmd.Parameters.AddWithValue("egg_image", imageBytes);  // bytea에 바이너리 직접 저장
                    cmd.Parameters.AddWithValue("egg_image_url", imageUrl ?? (object)DBNull.Value);

                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }

            System.Diagnostics.Debug.WriteLine($"[SaveInspection] DB 저장 완료");
            return true;
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

            // 파일명: {userId}_{timestamp}.jpg
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var fileName = $"{userId}_{timestamp}.jpg";
            System.Diagnostics.Debug.WriteLine($"[UploadStorage] 파일명 생성: {fileName}");

            // eggs 버킷에 업로드 시도
            System.Diagnostics.Debug.WriteLine($"[UploadStorage] Storage 업로드 시작...");

            var uploadResponse = await client.Storage
                .From("eggs")
                .Upload(imageBytes, fileName, new Supabase.Storage.FileOptions
                {
                    ContentType = "image/jpeg",
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
