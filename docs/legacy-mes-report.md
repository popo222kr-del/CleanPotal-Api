# MES 조회 (기존 MES DB 읽기 전용)

메뉴: MAIN › MES 조회 — 삼성 입·출고 현황 · 기타 입·출고 현황 · 출하실적 현황 · 제품 단가. **OFFICE 업무 조회 권한**이 있어야 보인다(금액·단가).

## 연결

운영 서버 `appsettings.local.json` 에만 넣는다(대화·저장소에 올리지 않는다).

```json
"ConnectionStrings": {
  "Default": "(기존 그대로)",
  "LegacyMes": "Server=MES DB 주소,포트;Database=DB 이름;User Id=읽기전용계정;Password=…;TrustServerCertificate=True;Application Name=CleanPotal-Report"
},
"LegacyMes": {
  "Customers": "*"
}
```

- 계정은 **읽기 전용**(db_datareader)으로 받는다. 코드에도 정해 둔 SELECT 만 있다(바깥에서 SQL 을 받지 않는다).
- 모든 표에 `WITH (NOLOCK)`, 명령 시간 60초, 결과 최대 2만 건, 재공 1분·출하/품목 5분 캐시 — 현장 MES 를 느리게 하지 않게.
- 연결이 없거나 실패하면 화면에 안내가 뜨고(503), 서버 로그에 `[legacy-mes]` 경고(오류 번호만, 접속 정보는 남기지 않음).

## 화면과 데이터 기준 (1차 — 사용자가 쓰던 조회 쿼리 기준)

| 화면 | 기준 |
|---|---|
| 입·출고 현황 › 재공 | `MWIPLOTSTS` FACTORY='AF01' 인 LOT. **삼성**(`/mes-report/wip`)은 구분 MEMORY → FOUNDRY, **기타**(`/mes-report/wip-other`)는 기타세정을 고객사별로 묶는다. 묶음 아래 라인(LOT_CMF_7) → PROCESS(LOT_CMF_8 공정명) → 제품군 → 품목 × 공정(OPER)으로 센다. 라인·PROCESS·제품군 칸은 같은 값이 이어지면 셀 병합(rowSpan)으로 한 칸에 표시. 라인마다 흰색·회색 번갈아 표시. 숫자 칸 어디를 눌러도 LOT 목록이 열리고, 목록은 [새 창으로 보기](`/mes-report/lots-view`, 같은 조건으로 다시 불러와 거름)로 따로 띄울 수 있다. LOT 목록에서 AETS 출고일 칸은 뺐다. 표는 화면에서 LOT 목록으로 직접 만들어, 어느 숫자를 눌러도 그 숫자만큼의 LOT 가 나온다. 조회 조건은 구분·라인·PROCESS·제품군·품목코드·품명 따로 |
| 입·출고 현황 › 세정 이력 조회 (삼성/기타 각각 해당 LOT 만) | 기존 MES '세정 이력 조회' 화면 기준. AETS 입고일(CREATE_TIME) 기간 + WIP(AF01)/SHIP(CUSTOMER) + LINE·MAT_TYPE·MAT_ID·S/N(반출·반입)·PROCESS·OUT NO·설비명·분임조·LOT ID 조건(모두 SQL 안에서 LIKE 포함 검색). 기간만이면 1년, 조건이 있으면 3년까지. 아직 DB 칸을 못 찾은 것: SEC 반입라인·무게·세정상태·혼입체크·공정별 시각(입고·입고검사·세정작업·DRY·BAKE OVEN·출하검사)·입/출하 검사·출하예정일·TAT·COMMENT, 조건 수리제품·ABNORMAL·합불여부·세정 지연시간 |
| 출하실적 현황 | FACTORY='CUSTOMER' 이고 AETS 출고일(LOT_CMF_6)이 그 해인 LOT. 고객사(MAT_CMF_1) → 반출라인 → 월. 수량 = LOT 수, 금액 = 품목 단가(MAT_CMF_4) 합 |
| 제품 단가 | `MWIPMATDEF` AF01 품목과 MAT_CMF_4 |

고객사 이름은 MES 공통 코드표 `MGCMTBLDAT`(FACTORY='AF01', TABLE_NAME='CUSTOMER')의 `KEY_1`(코드)·`DATA_1`(이름)에서 30분마다 읽는다. 칸 이름이 다르면 `LegacyMes:CustomerKeyColumn` / `CustomerNameColumn`, 이름을 바꿔 보이고 싶으면 `LegacyMes:CustomerNames`("10001=MEMORY,…")로 덮어쓴다.

고객사는 `LegacyMes:Customers` 로 거른다 — `"*"`(또는 줄을 빼면) 전체 고객사, `"10001,10002"` 처럼 적으면 그 코드만. 화면의 고객사 목록은 품목에 쓰인 MAT_CMF_1 전체.

## 설정 더

- `LegacyMes:CustomerGroups` — 구분 순서와 이름(기본 `"10001=MEMORY,10002=FOUNDRY"`, 나머지는 기타세정)
- `LegacyMes:MatGroupColumn` — 제품군(Accessory·QTZ Boat …)이 든 MWIPMATDEF 칸 이름(기본 `MAT_TYPE`)

## 기존 프로그램과 맞춰야 할 것 (확인 후 반영)

- 출하 수량의 소수(0.25 등)·금액 계산 방식 — 기존 화면이 쓰는 저장 프로시저 내용(`sp_helptext`)을 확인해 맞춘다.
- 입출고 현황의 PROCESS(A급·ALD ALO 등)·MAT GROUP 칸 — 어느 칸인지 확인 후 추가.
- 제품 단가의 적용일자별 이력(`CMATPRICE` 로 보임)·제품 사진(기존 MES 업로드 시 원본이 옮겨지는 위치) — 확인 후 추가.
- 창고 재고 칸(동탄·천안, 세정 완료/장기)은 이번 범위에서 뺐다.

## 칸 확인 결과 (2026-10-13, 실제 LOT 1SA261013024P · 1SB261013003P 대조)

| 화면 칸 | DB 칸 | 비고 |
|---|---|---|
| 반출 S/N / 반입 S/N | MWIPLOTSTS.LOT_CMF_1 / LOT_CMF_14 | |
| 사용횟수 | LOT_CMF_2 | |
| 반출번호 | LOT_CMF_4 - LOT_CMF_5 | TB_OUT_LIST_IF.OUTNO/OUTSEQ 와 연결 |
| **출하예정일** | LOT_CMF_6 | 공정이 넘어갈 때마다 다시 계산되는 목표 시각. **출고일이 아님** |
| AETS 출고일 | SHIP_TIME (출하된 LOT 만), 없으면 LOT_CMF_6 | 출하 LOT 로 기존 화면과 대조 필요 |
| SEC 반출라인 · 공정명 · PM 설비명 · 유/무상 · 분임조 | LOT_CMF_7 · 8 · 9 · 10 · 13 | 분임조는 TB_OUT_LIST_IF.CIRCLENM 과 같음 |
| (Y/N) | LOT_CMF_11 | TB_OUT_LIST_IF.ISSERIAL(시리얼 관리 여부)과 같음 — 재세정 아님 |
| **누적(A)** | TB_OUT_LIST_IF.CBM_WET 등 CBM_* | LIFE 칸은 비어 있음 → CBM_* 중 처음 값 |
| SEC 출고일 | TB_OUT_LIST_IF.OUTDAY | |
| 입고 · 입고검사 · 세정작업 · DRY · BAKE OVEN · 출하검사 | MWIPLOTHIS — 입고 = CREATE 의 TRAN_TIME, 나머지 = OLD_OPER 2100 · 3000 · 4000 · 5000 · 7000 인 END 의 TRAN_TIME | 그 공정을 **끝낸** 시각(1SB261013003P 입고 11:41:18 · 입고검사 15:42:10 기존 화면과 일치). 작업자 = TRAN_USER_ID(사번) |
| COMMENT | MWIPLOTSTS.LAST_COMMENT | |

아직 못 찾음: 무게(Kg) · 세정상태 · 혼입체크 · SEC 반입라인 · 입/출하 검사 · TAT. 출하실적 현황의 월 기준도 아직 LOT_CMF_6(출하 LOT 로 SHIP_TIME 과 대조 후 바꿈).
