using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TerrainCollapse.Networking;
using UnityEngine;

namespace TerrainCollapse
{
    // 서버를 가볍게 만들면서 클라이언트가 새로 맡게 된 두 가지 기능
    // 1. 지형 만들기 : 서버가 지형(맵)을 보내 주지 않음. 시드를 받아 여기서 만듬. 어차피 똑같음.
    // 2. 굴착 대기 관리 : 서버 확정을 기다리는 요청을 칸 단위로 들고 있음 => 쌤 클로드가 보완해서 맹글어놓음
    public sealed partial class RoomClient
    {
        #region 지형 만들기

        // 지형은 더이상 서버에서 받지 않는다. 서버가 준 시드로 여기서 제작
        //
        // Cluade Log
        // : 예전에는 이 자리에서 서버가 지형 전체(실측 482KB)를 보내 주기를 기다렸다.
        // 지금은 시드가 담긴 map_session 하나(164바이트)만 오고 지형은 각자 만든다.
        // 서버와 클라이언트가 같은 표를 같은 순서로 쓰기 때문에 결과가 한 칸도 다르지 않다.
        //
        // 서버의 ServerTerrainGenerator와 같은 규칙이므로 변경 시 양쪽을 함께 수정해야 한다.
        private void HandleMapSession(MapSessionMessage message)
        {
            MapSession = message?.Session;

            if (MapSession?.IsValid() != true)
            {
                Debug.LogError("[RoomClient] 맵 세션 정보가 없어 지형을 만들 수 없습니다.", this);
                _ = DisconnectAsync();
                return;
            }

            string localDataVersion;
            try
            {
                localDataVersion = ComputeTerrainDataVersion();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"[RoomClient] 지형 데이터 버전을 계산하지 못했습니다: {exception.Message}",
                    this);
                _ = DisconnectAsync();
                return;
            }

            if (string.Equals(
                    localDataVersion,
                    MapSession.TerrainDataVersion,
                    StringComparison.OrdinalIgnoreCase) == false)
            {
                Debug.LogError(
                    $"[RoomClient] 서버와 지형 데이터 버전이 다릅니다. " +
                    $"client={localDataVersion}, server={MapSession.TerrainDataVersion}",
                    this);
                _ = DisconnectAsync();
                return;
            }

            Debug.Log(
                $"[RoomClient] Map session received: {MapSession.MapSessionID} " +
                $"(profile={MapSession.ProfileID}, seed={MapSession.Seed})",
                this);

            TerrainMapFile map = TerrainSeedGenerator.Generate(
                MapSession.ProfileID,
                MapSession.Seed);

            ApplyTerrainSnapshot(TerrainNetworkAdapter.FromGeneratedMap(map));
        }

        private static string ComputeTerrainDataVersion()
        {
            string dataRoot = Path.Combine(
                Application.streamingAssetsPath,
                "Table",
                "Terrain");

            using SHA256 hash = SHA256.Create();
            foreach (string path in Directory
                         .GetFiles(dataRoot, "*.tsv", SearchOption.TopDirectoryOnly)
                         .OrderBy(value => value))
            {
                byte[] bytes = ReadNormalizedTsvBytes(path);
                hash.TransformBlock(bytes, 0, bytes.Length, null, 0);
            }

            hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return BitConverter
                .ToString(hash.Hash)
                .Replace("-", string.Empty)
                .ToLowerInvariant()
                .Substring(0, 12);
        }

        private static byte[] ReadNormalizedTsvBytes(string path)
        {
            string content = Encoding.UTF8.GetString(File.ReadAllBytes(path));
            content = content.TrimStart('\uFEFF')
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');

            if (content.EndsWith("\n", StringComparison.Ordinal))
                content = content[..^1];

            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content);
        }

        // Cluade Log
        // 서버가 잡아 둔 낙하를 무를 때 온다. 번호만 온다.
        //
        // 예약을 푸는 것은 지형을 바꾸지 않는다. 칸은 서버에 그대로 있다.
        // 그래서 지형을 다시 받을 일이 없고, 떨어지던 덩어리만 제자리로 돌리면 된다.
        private void HandleCollapseCancelled(TerrainCollapseCancelledMessage message)
        {
            if (collapseManager == null || message?.CollapseIDs == null) return;

            ClearPendingCollapsePlacements(message.CollapseIDs);

            foreach (long collapseID in message.CollapseIDs)
                collapseManager.CancelChunk(collapseID);

            collapseManager.RestoreCells(
                TerrainNetworkAdapter.ToRuntimeCells(message.RestoreCells));

            if (terrainManager != null)
                terrainManager.NotifyCollapseCancelled(message.CollapseIDs);
        }

        #endregion

        #region 굴착 대기 관리

        // 서버 응답 지연이나 유실이 전체 채굴 입력을 잠그지 않도록 요청별로 추적한다.

        // 서버 확정을 기다리는 굴착. RequestId -> 보낸 시각.
        //
        // 예전에는 슬롯이 하나였다. 그래서 응답을 한 번 놓치면 그 칸이 아니라
        // 굴착 전체가 영영 잠겼다. 화면에서는 "지형이 안 바뀌는" 것으로 보인다.
        private readonly Dictionary<string, float> pendingExcavations = new();

        // 같은 칸을 확정 전에 두 번 파지 않기 위한 목록이다.
        private readonly Dictionary<string, Vector2Int> pendingExcavationCells = new();

        private readonly List<string> timedOutRequestIds = new();

        // 답이 영영 오지 않아도 잠기지 않게 하는 안전장치다. 정상 왕복은 0.2초 안쪽이다.
        private const float PendingTimeoutSeconds = 3f;

        // 한 번에 서버에 띄워 둘 수 있는 굴착 수.
        private const int MaxInFlightExcavations = 8;

        private void ClearPendingExcavation(string requestId)
        {
            if (string.IsNullOrEmpty(requestId)) return;

            pendingExcavations.Remove(requestId);
            pendingExcavationCells.Remove(requestId);
        }

        private void ClearAllPendingExcavations()
        {
            pendingExcavations.Clear();
            pendingExcavationCells.Clear();
        }

        private void ClearPendingCollapsePlacement(string requestId)
        {
            if (string.IsNullOrEmpty(requestId)) return;

            pendingCollapsePlacementIds.Remove(requestId);
        }

        private void ClearAllPendingCollapsePlacements()
        {
            pendingCollapsePlacementIds.Clear();
        }

        private void ClearPendingCollapsePlacements(IReadOnlyList<long> collapseIds)
        {
            if (collapseIds == null || collapseIds.Count == 0) return;

            timedOutRequestIds.Clear();
            foreach (KeyValuePair<string, long> pending in pendingCollapsePlacementIds)
            {
                for (int i = 0; i < collapseIds.Count; i++)
                {
                    if (pending.Value != collapseIds[i]) continue;

                    timedOutRequestIds.Add(pending.Key);
                    break;
                }
            }

            foreach (string requestId in timedOutRequestIds)
                ClearPendingCollapsePlacement(requestId);
        }

        /// <summary>이 RequestId 로 띄워 둔 대기만 푼다. 남의 것은 건드리지 않는다.</summary>
        private void ReleasePendingFor(string requestId)
        {
            if (string.IsNullOrEmpty(requestId)) return;

            ClearPendingExcavation(requestId);

            ClearPendingCollapsePlacement(requestId);
        }

        // 답이 오지 않은 요청을 시간으로 푼다.
        // 이것이 없으면 응답 하나를 놓쳤을 때 그 칸을 영영 못 판다.
        private void SweepPendingTimeouts()
        {
            float now = Time.unscaledTime;

            if (pendingExcavations.Count > 0)
            {
                timedOutRequestIds.Clear();

                foreach (KeyValuePair<string, float> pending in pendingExcavations)
                    if (now - pending.Value > PendingTimeoutSeconds)
                        timedOutRequestIds.Add(pending.Key);

                foreach (string requestId in timedOutRequestIds)
                {
                    Debug.LogWarning(
                        $"[RoomClient] 채굴 응답이 늦어 대기를 풉니다: {requestId}",
                        this);
                    ClearPendingExcavation(requestId);
                }
            }

            // 낙하 배치는 입력을 잠그지 않는다. 응답이 늦더라도 RequestId를 지우면
            // 늦게 도착한 서버 batch를 내 배치로 인식하지 못하므로, 성공/오류/취소 시에만 정리한다.
        }

        #endregion
    }
}
