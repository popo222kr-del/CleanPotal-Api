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
| 입·출고 현황 › 세정 이력 조회 (삼성/기타 각각 해당 LOT 만) | 기존 MES '세정 이력 조회' 화면 기준. AETS 입고일(CREATE_TIME) 기간 + WIP(AF01)/SHIP(CUSTOMER) + LINE·MAT_TYPE·MAT_ID·S/N(반출·반입)·PROCESS·OUT NO·설비명·분임조·LOT ID 조건(모두 SQL 안에서 LIKE 포함 검색). 기간 길이 제한 없음(최근 입고 순 최대 2만 건). 아직 DB 칸을 못 찾은 것: SEC 반입라인·무게·세정상태·혼입체크·공정별 시각(입고·입고검사·세정작업·DRY·BAKE OVEN·출하검사)·입/출하 검사·출하예정일·TAT·COMMENT, 조건 수리제품·ABNORMAL·합불여부·세정 지연시간 |
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

### 세정 이력 칸 (2026-10-13 2차 — 출하 완료 LOT 이력으로 확인)

- MWIPLOTHIS TRAN_CODE: `CREATE`(입고) · `START`(공정 시작, RES_ID = 설비) · `END`(OLD_OPER 공정 완료 → OPER 다음 공정) · `SHIP`(출하).
- 입고 = CREATE · 입고검사/출하검사 = END(OLD_OPER 2100/7000) · 세정/DRY/BAKE = START(OPER 3000/4000/5000) ~ END(OLD_OPER 같음), 설비 = RES_ID(세정 MDC03, DRY STOCKER MBS01 …), 경과(h) = OUT − IN.
- AETS 출고일 = SHIP 이력 시각(없으면 SHIP_TIME → LOT_CMF_6).
- 작업자 = TRAN_USER_ID → MSECUSRDEF(USER_ID → LegacyMes:UserNameColumn, 기본 USER_DESC). 이름을 못 찾으면 사번(0805 같은 PDA 계정 포함) 그대로.
- COMMENT = 마지막 TRAN_COMMENT(없으면 LAST_COMMENT).
- 측정값 = CEDCLOTDAT(FACTORY, LOT_ID, HIST_SEQ, PARAMETER_ID, OPER, VALUE_01~20). WEIGHT@2100 = Weight IN, 그 밖의 공정 WEIGHT = OUT, 식각량 = IN − OUT. CHIP·CRACK·PIT·SCR·STAIN·STRIP·UNETCH(외관, Y/N), NTHK@RCV = 누적.
- LOT ID 를 누르면 LOT 상세 새 창(`/mes-report/lot/:lotId`, API `GET api/mes-report/lot/{lotId}`) — 이력 전체·측정값 전체.
- 세정상태 = CEDCLOTDAT PARAMETER_ID 'RESULT'(합부판정) — 여러 번이면 HIST_SEQ 가 가장 큰 값(1SA260901035P 기존 화면 OK).
- 아직: 세정 RECIPE·재세정 RECIPE·약품사용횟수·PDA·Particle/Roughness/Thickness 항목 이름·TAT 계산식·혼입체크. SEC 반입라인 출처 재확인.

### LOT 현황 조회 · SEC 반입라인 (2026-10-14)

- 입·출고 현황의 [LOT 현황 조회] 탭 — LOT ID · S/N(반출·반입) · 반출번호(`C0204899013` 또는 순번까지 `C0204899013-1`) 중 하나로 찾는다. 앞부분 일치, 기간 없음, 최대 300건.
  [조회]를 누르면 새 창(`/mes-report/lot-find`)이 열리고 한 건이면 바로 LOT 상세, 여러 건이면 목록에서 고른다. API `GET api/mes-report/lot-search`.
- SEC 반입라인 = 반출 표 TB_OUT_LIST_IF.SHIPTO(반입 위치, "6LINE 3층 C반입구")의 라인 부분 — 출하 관리(SCM) 화면과 같다(C0204393193: 반출 7LINE / 반입 6LINE).
  기존 '세정 이력 조회' 화면은 반입라인 칸에 반출라인을 그대로 보여 줬다. 반입 위치가 없으면 반출라인. 다르면 표에서 빨간색.

### LOT 상세 (2026-10-14 2차)

- LOT 현황 조회 목록에서 들어가면 [← 목록] 단추(같은 창에서 목록으로).
- SEC 출고일 칸 뺌.
- 측정값을 입고검사(RCV·2000·2100) | 출하검사(그 밖, 최종검사 7000 등) 반반으로. 같은 항목은 마지막(HIST_SEQ 큰 것)만, 입력자·시각은 머리에 한 번.
- 스펙(MIN·MAX) = 품목 측정 기준 표 CEDCMATPRM(FACTORY, MAT_ID, PARAMETER_ID, OPER, VALUE_COUNT, MIN_VALUE, MAX_VALUE, VISUAL_TYPE, DISABLE_FLAG …) — 확인함(600054048).
  공정(OPER 2100·7000·RCV)별로 맞추고 DISABLE_FLAG = Y 는 뺀다. 설명 칸은 없어 화면의 항목 이름표를 쓴다.
- 빨간색: 스펙 밖 숫자 · 외관 항목 Y · 합부판정 NG.

## 삼성 입고검사 이력 (세정 작업 › 삼성 입고검사 이력 맨 위, `/incoming-insp`, 새 창 `/incoming-insp/window`) — 2026-10-14

- 입고검사하면서 옆 모니터로 그 부품(같은 반출 S/N)의 지난 회차를 본다. 세정 작업 조회 권한, 단가·금액 없음. API `api/incoming-insp` (ViewHandover, MenuGate `/incoming-insp`).
- 왼쪽 **입고검사 대기**: 재공 중 OPER **2100** · **삼성(구분 MEMORY·FOUNDRY 고객사)만**. 라인별로 묶어 두 줄씩(시각·품명·회차·⚠/NEW / 세정코드·S/N·'외관 이상 이력'·'NG 이력'), [⚠ 외관 이상] 거르기. 어느 항목·회차인지는 마우스를 올리거나 눌러 오른쪽에서. 목록 너비는 끌어서 조절(PC 에 기억), [새 창으로 보기]. 같은 S/N 의 **최근 출하 5회** 측정값으로 표시 — "NG 10회" · "SCR 8·9회"(외관 항목은 MES 이름 그대로 CHIP·CRACK·SCR·STAIN·PIT·STRIP·UNETCH) · 첫 입고. 1분 캐시, 화면도 1분마다 새로.
- 위 **검색칸**(바코드 스캐너 = 키보드): LOT ID(정확히) → S/N(반출·반입) → 반출번호 순으로 찾아 가장 최근 입고 LOT 을 '이번'으로.
- 오른쪽 **비교표**: 이번 + 이전 5회(입고 시각 순). 입고일→출고일 · LOT ID · 상태 · 입고 외관(Y 인 항목만) · 무게 IN→OUT · 식각량 · 표면먼지 · 출하 외관 · 판정 · COMMENT. 스펙 밖·외관 이상·NG 는 빨간색.
- **경고**: 직전 판정 NG, 지난 회차 외관 이상, 스펙 밖 값(CEDCMATPRM 기준). 계산은 `IncomingInspection`(시험 `IncomingInspectionTests`).
- **한글 입력기로 찍힌 바코드**: 입력기가 한글이면 스캐너 키가 한글로 들어온다("1SN261014004P" → "1누261014004ㅔ"). 화면(`components/koreanKeys.ts`)과 서버(`KoreanKeys`) 둘 다
  두벌식 자판 기준으로 눌린 영문 키(대문자)로 되돌린다. LOT 현황 조회도 같다. Enter 가 조합 중에 오면 조합이 끝난 뒤 찾는다.

## 출하실적 현황 개편 (2026-10-14)

- 기존 MES '출하실적 현황(차트)' 모양: 고객사(구분 — 삼성은 MEMORY·FOUNDRY, 나머지는 고객사 이름) → 라인(반출라인) → 1~12월 · 합계 · 동탄창고 · 천안창고. 줄마다 위 수량 / 아래 금액.
- 월 = **실제 출하 시각 SHIP_TIME**(없으면 LOT_CMF_6). 예전엔 LOT_CMF_6(출하예정일) 기준이라 월이 어긋날 수 있었다. 한 해 상한 15만 LOT(넘으면 화면 경고).
- 창고 칸 = 지금 재고(재공 OPER 8100 동탄창고 · 8000 천안창고). 기존 화면의 '세정된 재고 / 장기 재고' 나눔은 기준 확인 전이라 합쳐서.
- 숫자를 누르면 세부내역 창(`GET api/mes-report/shipments/detail`, 표와 같은 캐시 자료를 같은 조건으로): 맨 위 TYPE별 개수·금액(누르면 그 TYPE 만), 그 아래 품목별 수량·금액(누르면 그 품목만), 아래 LOT 목록(입고일·출고일·반출/반입라인·PM설비·분임조·공정·TYPE·품명·세정코드·S/N·수량·금액), 엑셀.
- 수량 = **LOT 안의 개수 MWIPLOTSTS.QTY_1**(없거나 0 이면 1, `LegacyMesReports.QtyOf`). 삼성 LOT 은 1, 기타세정 노즐·분석 의뢰 등은 24·60·100 처럼 여러 개. 기존 MES 의 세트 품목 0.25 '표시'는 따르지 않음(금액은 원래 같았음).
- 금액 = 단가 × 수량(QTY_1). 단가 = **단가표 CMATPRICE(FACTORY, MAT_ID, APPLY_DATE, MAT_PRICE)에서 입고일(CREATE_TIME)에 적용 중인 값**(적용일 ≤ 입고일 중 가장 최근 — 기존 MES 와 같음, WOOAM 01-13 단가 변경으로 확인), 없으면 품목 마스터 MAT_CMF_4, 그것도 없으면 0.
  창고 재고도 입고일 단가. 단가표는 30분 캐시(`PriceBook`). 세부내역에 LOT 별 단가 칸. (CMATPRICE 를 쓰는 DB 프로시저는 없음 — 기존 프로그램 안에서 계산)

- **AF01 품목만 집계** (2026-10-14): 품목 마스터(MWIPMATDEF, FACTORY='AF01')에 없는 품목의 LOT(예: AF04 Kioxia 시험 품목 301040026 등 — 품명·TYPE·단가·고객사가 비어 '(없음)'으로 보이던 것)은
  **모든 MES 조회**(재공·출하 집계, 세정 이력·LOT 현황 조회, LOT 상세, 입고검사 이력)에서 뺀다 — LOT 을 읽는 `LotsAsync` 에서 한 번에(`OwnMaterial`).

### 2026-01 기존 MES 대조 결과 (LOT 단위, `출하실적_2026-01_MES_웹_대조.xlsx`)

- MES 930,318,750 / 웹(LOT=1 시절) 876,243,750, 차이 −54,075,000.
- 같은 LOT 5,608건(98.7%) 금액 같음 — MES 수량 0.25 로 보이는 4,466건도 금액은 같았다(0.25 는 표시만).
- 금액 다른 74건 −53,383,100: 기타세정 품목에서 MES = 단가 × QTY_1(24·58·60·70·100 확인) → QTY_1 반영으로 해소.
- MES 에만 37건 2,960,000(SEMES_SD_1 소분, 라인 빈칸) · 웹에만 13건 2,268,100(RETAINER RECYCLE 등) — 기존 화면의 SD-1·Retainer Ring 체크 기준 확인 필요.
- 2차(QTY_1 반영 후): MES 930,318,750 / 웹 929,496,850, 차이 −821,900 — 단가 2건(−130,000, 입고일 기준으로 해소) · MES 에만 SD_1 소분 37건 · 웹에만 13건.
- 실제 매출(세금계산서)은 T_TaxListSub(LOT_ID, Good_Volume, Good_Price, Good_Sum, S_Date, EX_Date …)로 확인 가능.

### 2026-01 3차 — 남은 차이 원인과 반영 (2026-10-14)

- 웹에만 있던 13건: **무상세정 6건**(기존 MES 는 무상 제외) · RETAINER RECYCLE(700021115, 유상) 6건 · 기타 공용 분석_토모 1건.
  → 출하실적 기본값 **무상세정 제외**(`IsFree`: LOT_CMF_10 에 '무상'). [무상세정 포함] 체크로 넣을 수 있다. 세부내역에 유/무상 칸.
- MES 에만 있던 37건(SEMES_SD_1 소분): 세정 LOT 이 아니라 **케미칼 출고 CCHEMICALOUT**(FACTORY, LOT_ID, LOT_SEQ, PAT_DATE, SERIAL_NO, OUT_DATE, OUT_USER_ID, PO_NO, CREATE_TIME, CREATE_USER_ID, TAX_DATE)
  — 기존 MES [소분관리 › 케미칼 출고등록]. → 출하실적 기본값 **케미칼(SD-1) 포함**(`ChemicalOutAsync`: 1건 = 수량 1, 입고일 PAT_DATE, 출고일 OUT_DATE,
  품목 = CCHEMICALIN 의 LOT_ID → MAT_ID, 못 읽으면 LegacyMes:ChemicalMatId 기본 900050011). [케미칼(SD-1) 포함] 체크로 뺄 수 있다.
- 남는 것: RETAINER RECYCLE 6건(210,000 — 기존 화면 Retainer Ring 체크 기준 확인 필요) · 기타 공용 분석_토모 1건(300,000).

### 2026-01 4차 — 부적합(세정 SKIP + 출하검사 NG) 제외 (2026-10-14)

- RETAINER RECYCLE 6건: 입고검사(2100) PIT → **SKIP 2100→7000**(세정·건조 없음) → 출하검사(7000) **RESULT = NG** → 반출. 세정 없이 돌려준 부적합이라 MES 매출에 없다.
- 1월 출하 중 7000 RESULT = NG 68건을 MES 1월 파일과 대조:
  - SKIP 있는 9건(RETAINER 6 · Q400 NC_CU · YSQ MANIFOLD COVER · YSQ OUTER TUBE) → **모두 MES 없음**
  - SKIP 없는 59건(세정 후 NG — BOAT·DOME·TUBE 등) → **모두 MES 있음**
  → 출하실적 기본값 **세정 SKIP + 마지막 7000 RESULT = NG 인 LOT 제외**(`ShippedNgAsync`). [부적합(NG) 포함] 체크로 넣을 수 있다.
- 남는 것: 기타 공용 분석_토모 1SA260115046P(300,000) — NG·SKIP 아님, 원인 별도 확인.
