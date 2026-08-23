using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;
using UnityEngine;

namespace TerrainCollapse
{
    // TSV 헤더를 DTO의 public 필드에 매핑하여 데이터 행 목록으로 반환하는 공통 리더

    public static class TerrainTsvReader
    {
        private static readonly CsvConfiguration TsvConfig = new(CultureInfo.InvariantCulture)
        {
            Delimiter = "\t",
            Mode = CsvMode.NoEscape,
            HasHeaderRecord = true,
            MissingFieldFound = null,
            HeaderValidated = null,
            MemberTypes = MemberTypes.Properties | MemberTypes.Fields
        };

        /// <summary>지정 경로의 TSV 파일을 DTO 목록으로 변환</summary>
        public static List<T> ReadTable<T>(string filePath) where T : new()
        {
            if (File.Exists(filePath) == false)
            {
                Debug.LogError($"[TerrainTsvReader] 파일이 존재하지 않습니다: {filePath}");
                
                return null;
            }

            try
            {
                using StreamReader reader = new(filePath);
                using CsvReader csv = new(reader, TsvConfig);
                List<T> records = new();

                foreach (T record in csv.GetRecords<T>())
                    records.Add(record);

                return records;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[TerrainTsvReader] {filePath} 로딩 실패: {exception.Message}");
                
                return null;
            }
        }
    }
}
