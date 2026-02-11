using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EggClassifier.Models;

namespace EggClassifier.Services
{
    /// <summary>
    /// Supabase PostgreSQL 데이터베이스 연동 서비스 (egg, users 테이블)
    /// </summary>
    public interface ISupabaseService : IDisposable
    {
        /// <summary>
        /// 데이터베이스 연결 성공 여부
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 계란 판별 결과를 데이터베이스에 저장합니다.
        /// </summary>
        /// <param name="detection">탐지 결과 (Detection 객체)</param>
        /// <param name="inspectDate">판별 시각</param>
        /// <param name="userId">판별을 수행한 유저 ID (기본값: "system")</param>
        /// <param name="imageData">계란 이미지 바이너리 데이터 (선택, JPEG/PNG 바이트 배열)</param>
        /// <returns>저장 성공 여부</returns>
        Task<bool> SaveEggResultAsync(Detection detection, DateTime inspectDate, string userId = "system", byte[]? imageData = null);

        /// <summary>
        /// 여러 계란 판별 결과를 일괄 저장합니다.
        /// </summary>
        /// <param name="detections">탐지 결과 리스트</param>
        /// <param name="inspectDate">판별 시각 (모든 레코드에 동일 시각 적용)</param>
        /// <param name="userId">판별을 수행한 유저 ID (기본값: "system")</param>
        /// <param name="imageData">계란 이미지 바이너리 데이터 (선택, 모든 레코드에 동일 이미지 적용)</param>
        /// <returns>저장 성공 여부</returns>
        Task<bool> SaveEggBatchAsync(List<Detection> detections, DateTime inspectDate, string userId = "system", byte[]? imageData = null);

        /// <summary>
        /// 최근 계란 판별 기록을 가져옵니다.
        /// </summary>
        /// <param name="count">가져올 개수 (기본값: 100)</param>
        /// <param name="userId">특정 유저의 기록만 조회 (null이면 전체 조회)</param>
        /// <returns>계란 판별 기록 리스트</returns>
        Task<List<EggRecord>> GetRecentEggRecordsAsync(int count = 100, string? userId = null);

        /// <summary>
        /// 기간별 계란 판별 통계 데이터를 가져옵니다.
        /// </summary>
        /// <param name="startDate">시작일</param>
        /// <param name="endDate">종료일</param>
        /// <param name="userId">특정 유저의 통계만 조회 (null이면 전체 조회)</param>
        /// <returns>통계 데이터</returns>
        Task<EggStatistics> GetStatisticsAsync(DateTime startDate, DateTime endDate, string? userId = null);

        /// <summary>
        /// 데이터베이스 연결을 테스트합니다.
        /// </summary>
        /// <returns>연결 성공 여부</returns>
        Task<bool> TestConnectionAsync();

        /// <summary>
        /// 에러 발생 이벤트
        /// </summary>
        event EventHandler<string>? ErrorOccurred;
    }
}
