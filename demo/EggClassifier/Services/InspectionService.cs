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
            // Mat → byte[] 변환
            var imageBytes = eggImage.ToBytes(".png");

            // DB에 저장
            var client = await _supabaseService.GetClientAsync();
            var inspection = new EggEntity
            {
                UserId = userId,
                EggClass = eggClass,
                Accuracy = accuracy,
                InspectDate = DateTime.Now,
                EggImage = imageBytes
            };

            var response = await client.From<EggEntity>().Insert(inspection);
            return response != null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SaveInspectionAsync 오류: {ex.Message}");
            return false;
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
