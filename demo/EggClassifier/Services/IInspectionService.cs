using OpenCvSharp;

namespace EggClassifier.Services;

/// <summary>
/// 계란 검사 로그 저장 서비스 인터페이스
/// </summary>
public interface IInspectionService
{
    /// <summary>
    /// 검사 결과를 DB에 저장
    /// </summary>
    /// <param name="userId">사용자 ID</param>
    /// <param name="eggClass">계란 클래스 (0: 정상, 1: 크랙, 2: 이물질, 3: 탈색, 4: 외형이상)</param>
    /// <param name="accuracy">정확도 (0~1)</param>
    /// <param name="eggImage">계란 이미지 (Mat)</param>
    /// <returns>저장 성공 여부</returns>
    Task<bool> SaveInspectionAsync(string userId, int eggClass, double accuracy, Mat eggImage);

    /// <summary>
    /// 사용자별 검사 로그 개수 조회
    /// </summary>
    Task<int> GetInspectionCountAsync(string userId);

    /// <summary>
    /// 사용자별 정상/불량 개수 조회
    /// </summary>
    Task<(int normal, int defect)> GetInspectionStatsAsync(string userId);
}
