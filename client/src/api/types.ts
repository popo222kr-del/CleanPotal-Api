// API DTO와 매칭되는 타입 (camelCase JSON)

// 권한: 영역 × 등급 (0=없음, 1=조회, 2=편집)
export type AccessLevel = 0 | 1 | 2;
export interface UserDto {
  id: number;
  username: string;
  realName: string;
  department: string;
  teamName: string;
  /** 직급(호칭) — 사원·주임·대리·과장·차장·부장·상무·전무·부사장·사장 */
  rank: string;
  /** 직위(맡은 일) — QA팀장·세정팀장 등 */
  jobTitle: string;
  email: string;
  phoneNumber: string;
  employeeNumber: string;
  hireDate: string;
  /** 입사일로 서버가 계산한 근속("8년 3개월"). 해석 불가면 빈 문자열 — 읽기 전용 */
  tenure: string;
  isResigned: boolean;
  resignDate: string;
  isAdmin: boolean;
  accessSchedule: AccessLevel;
  accessRoster: AccessLevel;
  accessHandover: AccessLevel;
  accessField: AccessLevel;
  accessMes: AccessLevel;
  accessOffice: AccessLevel;
  /** MES 세부 권한 코드를 쉼표로 이은 것 (예: 'Rollback,AdminProduct'). 등급과 다른 축이다 */
  mesPermissions: string;
  hiddenMenus: string;   // 숨긴 하위 메뉴 경로 JSON 배열 (예: '["/meeting"]')
}

export interface LoginResponse {
  token: string;
  expiresAt: string;
  user: UserDto;
}

// ── 근무표 ──
export interface RosterDayHeader {
  day: number;
  dayOfWeek: string;
  isWeekend: boolean;
  isHoliday: boolean;
}
export interface RosterCell {
  date: string;
  shiftType: string;
  isPredicted: boolean;
}
export interface RosterMember {
  name: string;
  jobTitle: string;
  cells: RosterCell[];
  totalWorkDays: number;
}
export interface RosterTeam {
  team: string;
  members: RosterMember[];
  dailyCounts: number[];
  grandTotal: number;
}
export interface RosterMonth {
  year: number;
  month: number;
  days: RosterDayHeader[];
  teams: RosterTeam[];
}
export interface StampedCell {
  name: string;
  date: string;
  shiftType: string;
}

// ── 팀 일정 / 달력 ──
/** 달력에서 쓰는 부서 (색·약칭은 서버가 정해 내려준다) */
export interface CalendarDept { id: number; name: string; shortName: string; color: string; /** 생산(교대) 팀이 있는 부서 — 교대 근무 표시 기본값 */ hasShift?: boolean; /** 내 부서(/api/depts) */ mine?: boolean; }

export interface TeamEvent {
  id: number;
  registeredBy: string;
  startDate: string;
  endDate: string;
  content: string;
  detail: string;
  createDate: string;
  depts: CalendarDept[];   // 여러 부서가 함께 들어갈 수 있다. 비어 있으면 부서 미지정
}
export interface CalendarBadge { text: string; kind: string; names: string[]; }
export interface CalendarDay {
  date: string;
  day: number;
  dayOfWeek: string;
  isWeekend: boolean;
  holiday: string;
  dayShift: string[];
  nightShift: string[];
  offShift: string[];
  badges: CalendarBadge[];
  events: TeamEvent[];
}
export interface CalendarMonth {
  year: number;
  month: number;
  days: CalendarDay[];
}

// ── 생산팀요청 ──
export interface ProdReq {
  id: number;
  requestDate: string | null;
  dueDate: string | null;
  status: string;
  category: string;
  location: string;
  requestDetail: string;
  requester: string;
  actionDate: string | null;
  actionDetail: string;
  assignee: string;
  createdAt: string;
  requestImages: string;   // base64 data URL 배열 JSON
  actionImages: string;
  rowVersion: number;      // 저장 시 그대로 돌려보낸다(동시 수정 감지)
  canDelete: boolean;      // 등록자 본인 또는 관리자 — 수정은 등급 2 면 누구나
}

// ── 업체 관리 ──
export interface Vendor {
  id: number; vendorName: string; category: string; isWeekly: boolean; isFavorite: boolean;
  basePath: string; linkUrl: string; addresses: string; managers: string;
  /** 같은 업체의 MES 쪽 자료(MesCustomers.Id). 잇지 않았으면 null */
  mesCustomerId: number | null;
  /** 등록 부서(조직도 Id·이름). 같은 업체라도 부서마다 따로 등록한다. */
  deptId?: number | null; deptName?: string;
}

/** MES 업체(생산관리). 업체 관리 화면이 포털 업체와 나란히 다룬다. */
export interface MesCustomer {
  customerId: number; customerCode: string; customerName: string; exportPrefix: string;
  lineDefinitionId: number | null; lineCode: string | null; isActive: boolean;
}
export interface MesLine { lineId: number; code: string; description: string; }

/** MES 일괄 등록 — 아직 MES 에 없는 업체 한 줄. mesCustomerId 가 있으면 새로 만들지 않고 잇는다. */
export interface VendorMesBulkRow {
  vendorId: number; vendorName: string; customerCode: string; exportPrefix: string;
  lineDefinitionId: number | null;
  /** LINE 이름. 목록에 없는 이름을 적으면 등록할 때 새로 만든다. */
  lineCode: string;
  mesCustomerId: number | null; mesCustomerName: string | null;
}
export interface VendorMesBulkPreview {
  rows: VendorMesBulkRow[]; linkedCount: number; totalCount: number; lines: MesLine[];
}
export interface VendorMesBulkFailure { vendorId: number; vendorName: string; message: string }
export interface VendorMesBulkResult {
  success: boolean; message: string; created: number; linked: number; failures: VendorMesBulkFailure[];
}

// ── 현장 재고 ──
export interface InventoryItem {
  id: number; orderNo: number; itemCode: string; category: string; unit: string; registeredDate: string;
  storageLocation: string; itemName: string;
  currentStock: string; currentStockDisplay: string;
  previousStock: string; previousStockDisplay: string; weeklyDeltaText: string; weeklyDeltaIsDecrease: boolean;
  appropriateStock: string; minOrderQty: string; supplier: string;
  orderDate: string; orderQty: string; expectedReceipt: string; memo: string;
  isOrdered: boolean; isLow: boolean; updatedAt: string;
}
export interface InventoryZone { zoneKey: string; zoneName: string; locations: string; items: InventoryItem[]; }
export interface InventorySnapshot { date: string; itemId: number; stock: string; }

// ── 견적서 (실제 quotations.json 구조) ──
export interface QuotationItem {
  id: number; no: number; description: string; partCode: string; standardSpec: string;
  listPrice: number; qty: number; amount: number;
}
export interface Quotation {
  id: number; quoteNo: string; rfqNo: string; company: string; attention: string;
  email: string; phone: string; quoteDate: string | null; validity: string;
  aetsManager: string; aetsPhone: string; aetsEmail: string; businessNo: string;
  remarks: string; memo: string; sourceFileName: string;
  createdBy: string; createdAt: string; lastModifiedBy: string; lastModifiedAt: string | null;
  total: number; items: QuotationItem[];
  /** 등록 부서(조직도 Id·이름) */
  deptId?: number | null; deptName?: string;
}
export interface QuotationSummary {
  id: number; quoteNo: string; rfqNo: string; company: string; quoteDate: string | null;
  validity: string; total: number; itemCount: number; aetsManager: string; createdAt: string;
  deptId?: number | null; deptName?: string;
}

// ── 견적 마스터 (단가표·템플릿·설정) ──
export interface ProductMaster {
  id: number; productName: string; partCode: string; spec: string; unitPrice: number;
  vendorName: string; unit: string; updatedBy: string; updatedAt: string;
  /** 등록 부서(조직도 Id·이름). 단가표는 부서마다 따로 둔다. */
  deptId?: number | null; deptName?: string;
}
export interface GlobalTemplate { id: number; productCode: string; productName: string; templatePath: string; }
export interface QuotationConfig {
  businessNo: string; address: string; tel: string; fax: string; signer: string; companyName: string;
}

// ── BROKEN 관리 ──
export interface BrokenRecord {
  no: number;
  id: number;
  occurDate: string | null;
  line: string;
  productName: string;
  productType: string;
  sn: string;
  team: string;
  causer: string;
  jobTitle: string;
  career: string;
  occurStage: string;
  description: string;
  status: string;
  isOfficial: boolean;
  positionFrozen: boolean;
  incidentReports: string;
  countermeasureReports: string;
  trainingDocs: string;
  trainingImages: string;
  createdAt: string;
  /** 동시 수정 감지용 버전 — 고쳐 저장할 때 그대로 돌려보낸다 */
  rowVersion?: number;
}
export interface BrokenFilterOptions {
  years: number[];
  teams: string[];
  productTypes: string[];
}
export interface BrokenTraining {
  id: number; trainingType: string; trainingDate: string | null; content: string; documents: string; images: string;
}
export interface BrokenGoal { id: number; category: string; year: number; target: string; }

// ── QR 체크시트 ──
export interface CheckPhoto { k: 'before' | 'after' | 'ng' | 'photo'; v: string; }
export interface CheckResult {
  id: number; result: '' | 'OK' | 'NG' | 'NA'; numValue: number | null; memo: string; photos: CheckPhoto[];
  checkedAt: string; checkedByName: string; ngStatus: '' | 'OPEN' | 'DONE';
}
export interface CheckSheetItem {
  itemId: number; code: string; group: 'common' | 'zone' | 'weekly' | 'event'; text: string; detail: string; timing: string;
  weekdayLabel: string; dueState: string; resultType: 'OKNG' | 'NUM'; unit: string; minValue: number | null; maxValue: number | null;
  judgeMode: string; specText: string; photoPolicy: string; required: boolean; allowNa: boolean; paperForm: string;
  result: CheckResult | null; doneElsewhere: string;
  /** 다른 교대에서 작업 전 사진을 올려 둔 '작업 중' 항목이면 그 교대 설명(예: 10/7(수) 주간 홍길동). */
  workingFrom?: string;
}
export interface CheckSheet {
  zoneCode: string; zoneName: string; line: string; workDate: string; shift: '주간' | '야간'; isCurrent: boolean;
  runId: number | null; submittedAt: string | null; submittedByName: string; canEdit: boolean; needsReason: boolean;
  items: CheckSheetItem[];
  /** 아직 시작하지 않은 교대 — 관리자도 입력하지 못한다. */
  isFuture?: boolean;
}
export interface CheckShiftStatus { state: 'none' | 'progress' | 'submitted' | 'na'; done: number; total: number; ng: number; submittedByName: string; submittedAt: string | null; }
export interface CheckZoneStatus { code: string; name: string; day: CheckShiftStatus; night: CheckShiftStatus; weeklyDue: number; weeklyOverdue: number; weeklyWorking?: number; }
export interface CheckStatus { workDate: string; currentShift: string; currentWorkDate: string; lines: { line: string; zones: CheckZoneStatus[] }[]; openNg: number; }
export interface CheckNg {
  resultId: number; zoneCode: string; zoneName: string; line: string; workDate: string; shift: string;
  itemCode: string; itemText: string; itemDetail: string; specText: string; numValue: number | null; memo: string;
  photos: CheckPhoto[]; checkedByName: string; checkedAt: string; ngDept: string;
  ngStatus: 'OPEN' | 'DONE'; ngClosedAt: string | null; ngClosedBy: string; ngCloseNote: string;
}
export interface CheckReportRow { zoneCode: string; zoneName: string; itemCode: string; text: string; detail: string; timing: string; paperForm: string; cells: string[]; }
export interface CheckReport {
  line: string; year: number; month: number; days: number; formName: string; revision: string; effectiveDate: string;
  rows: CheckReportRow[]; zones: { zoneCode: string; zoneName: string; due: number; done: number; ng: number; missing: number }[];
  ngs: CheckNg[]; generatedAt: string;
}
export interface CheckZoneDef {
  id: number; code: string; name: string; line: string; sortOrder: number; isCommon: boolean; hasQr: boolean;
  qrLocation: string; qrCount: number; isActive: boolean; note: string;
  /** 담당 부서(조직도 Id·이름). 다른 부서 구역은 점검·현황·리포트 모두 보이지 않는다. */
  deptId?: number | null; deptName?: string;
}
export interface CheckItemDef {
  id: number; code: string; zoneCode: string; sortOrder: number; text: string; detail: string; cycle: string; timing: string;
  weekday: number | null; resultType: 'OKNG' | 'NUM'; unit: string; minValue: number | null; maxValue: number | null; judgeMode: string;
  photoPolicy: string; required: boolean; allowNa: boolean; paperForm: string; ngDept: string;
  validFrom: string | null; validTo: string | null; revisionNote: string; isActive: boolean; note: string;
  updatedAt: string; updatedBy: string;
}
export interface CheckImportResult { zonesAdded: number; zonesUpdated: number; itemsAdded: number; itemsUpdated: number; warnings: string[]; }
export interface CheckQr { code: string; name: string; url: string; svg: string; }
export interface CheckQrPage { baseUrl: string; fromSetting: boolean; isLocal: boolean; suggestions: string[]; labels: CheckQr[]; overridden: boolean; savedUrl: string; }

// ── 회의록/보고서 (생산미팅·주간보고) ──
export interface ReportBlock {
  id: number; number: number; category: string; status: string;
  content: string; contentRich: string; followUp: string; followUpRich: string;
  kind: string; heading: string; isCollapsed: boolean; progressPercent: number; importance: string;
  followUpAttachments: string;
}
export interface Report {
  id: number; reportType: string; monthTitle: string; title: string; shortTitle: string; dateRange: string;
  memo: string; memoRich: string; mainContent: string; mainContentRich: string;
  nightContent: string; nightContentRich: string; attendees: string; summary: string;
  memoAttachments: string; mainAttachments: string;
  createdAt: string; updatedAt: string | null; blocks: ReportBlock[];
  creatorName: string;     // 과거 자료는 비어 있다(작성자 미상)
  rowVersion: number;      // 저장 시 그대로 돌려보낸다(동시 수정 감지)
  canDelete: boolean;      // 작성자 본인·관리자·작성자 미상 — 수정은 등급 2 면 누구나
}
export interface ReportSummary { id: number; title: string; shortTitle: string; dateRange: string; blockCount: number; hasMemo: boolean; hasContent: boolean; }
export interface ReportGroup { monthTitle: string; reports: ReportSummary[]; }

/// 특정 날짜의 주간/야간 근무 팀 (생산미팅 라벨)
export interface ShiftTeams { dayTeams: string[]; nightTeams: string[]; }

// ── 스케줄보드 ──
export interface ScheduleBlock {
  id: number; boardDate: string; equipmentIndex: number; startMinute: number;
  s2Minutes: number; hfMinutes: number; diMinutes: number; s2Temperature: number | null; recipeText: string;
}
export interface ScheduleRecipe {
  id: number; text: string; s2Minutes: number; hfMinutes: number; diMinutes: number;
  s2Temperature: number | null; isFavorite: boolean; orderIndex: number; displayText: string;
}
/** 스케줄보드 설비 묶음(MDC · MSC · NDC …). equipCount 가 0 이어야 지울 수 있다. */
export interface ScheduleGroup { id: number; name: string; orderIndex: number; equipCount: number }

export interface ScheduleEquipment {
  index: number; displayName: string; id: number; groupName: string; orderIndex: number;
  name: string; process: string; note: string; isIdle: boolean;
}

// ── 설비 ICP-MS ──
export const ICP_ELEMENTS = ['Li','Na','Mg','Al','K','Ca','Ti','Cr','Mn','Fe','Co','Ni','Cu','Zn','Ge','As','Cd','In','Ba','Ta','W','Pb'] as const;
export interface IcpmsEquipment { eqId: string; process: string; hasData: boolean; }
export interface IcpmsMeasurement {
  id: number; processType: string; eqId: string; bathGb: string; category: string; unit: string; analysisDate: string;
  values: Record<string, number>;
}
export interface IcpmsComparison { eqId: string; process: string; values: Record<string, number>; }
export interface IcpmsSummary {
  totalEquip: number; measuredEquip: number; unmeasuredEquip: number;
  latestDate: string; measuredDateCount: number;
  maxValue: number; maxEqId: string; maxElement: string; maxDate: string;
  average: number; unit: string;
}
export interface IcpmsFilters { processTypes: string[]; baths: string[]; eqIds: string[]; dates: string[]; }
export interface IcpmsCheckNote { eqId: string; process: string; measured: boolean; topElement: string; topValue: number; note: string; }
export interface IcpmsHistory { checkDate: string; note: string; updatedAt: string; }
export interface IcpmsActionLog { id: number; actionType: string; detail: string; userName: string; createdAt: string; }
export interface IcpmsUploadRow {
  processType: string; eqId: string; bathGb: string; category: string; unit: string; analysisDate: string;
  values: Record<string, number>;
}

// ── 교육 현황 대시보드 ──
export interface EducationPlan {
  id: number; memberName: string; courseName: string; startDate: string | null; endDate: string | null;
  status: string; progress: number; eduMethod: string; attachmentPath: string;
  /** 대상자의 부서(조직도 Id) */
  deptId?: number | null;
}

// ── 개인별 업무 분장표 ──
export interface WorkMember {
  id: number;
  username: string;        // WPF 에서 온 연결 키(사번). 계정·교육이수가 이 값으로 묶여 있다
  realName: string; department: string; teamName: string; jobTitle: string;
  employeeNumber: string;
  hireDate: string;
  tenure: string;            // 서버가 입사일로 계산한 경력 ("8년 3개월"). 못 구하면 빈 문자열
  email: string;
  phoneNumber: string;
  isResigned: boolean;     // 계정(User.isResigned) 기준 — 사용자 계정 관리와 같은 값
  resignDate: string;
  isHidden: boolean;
  hasAccount: boolean;       // false = 연결된 계정을 찾지 못함
  linkedUserId: number | null;  // 같은 값이 둘 이상이면 한 사람이 두 번 등록된 것
  accountCount: number;      // 이 행에 붙은 계정 수 — 중복 행 중 빈 쪽을 가려내는 데 쓴다
  eduCount: number;
}
export interface WorkAccount { id: number; username: string; serviceName: string; accountId: string; accountPassword: string; note: string; }
export interface WorkEdu {
  id: number; username: string; eduName: string; instructor: string; note: string;
  startDate: string; endDate: string;
  eduDate: string;       // WPF 원본에 없는 옛 단일 컬럼. 웹에서 직접 입력한 기록에만 값이 있다.
  eduDateText: string;   // 서버가 시작~종료를 합쳐 만든 표시용 문자열 (예: 2018-06-04~07)
}
export interface WorkMemberDetail {
  member: WorkMember; accounts: WorkAccount[]; edus: WorkEdu[];
  externalEdus: EducationPlan[];        // 교육 현황 대시보드에서 자동 연동 (여기서 편집하지 않음)
  externalEduNameAmbiguous: boolean;    // 동명이인 — 남의 교육이 섞여 보일 수 있다
}

// ── 배차 ──
export interface Dispatch {
  id: number; vendorName: string; outgoingDetails: string; incomingDetails: string;
  managerName: string; contactNumber: string; fullAddress: string; note: string; createDate: string;
  /** 동시 수정 감지용 버전 — 저장할 때 그대로 돌려보낸다 */
  rowVersion?: number;
}

// ── 사무실 공지 ──
// canModify: 작성자 본인 또는 관리자 — 공지는 수정·삭제 모두 작성자 책임 항목이다.
// rowVersion: 저장할 때 그대로 돌려보내면 그 사이 남이 먼저 고쳤는지 서버가 잡아준다.
export interface Notice {
  id: number; title: string; content: string; author: string; createdAt: string;
  rowVersion: number; canModify: boolean;
}

// ── 인수인계 ──
export interface Handover {
  id: number;
  vendor: string;
  category: string;
  owner: string;
  content: string;
  inDate: string | null;
  outDate: string | null;
  status: string;
  deliveryMethod: string;
  memo: string;
  isWeekly: boolean;
  progressPercent: number;
  creatorName: string;
  createDate: string;
  modifierName: string;
  modifyDate: string | null;
  isNewUpdate: boolean;   // 미확인(빨간 점)
  images: string;         // 첨부 이미지 JSON (base64 data URL 배열)
  rowVersion: number;     // 저장 시 그대로 돌려보낸다(동시 수정 감지)
  canDelete: boolean;     // 작성자 본인 또는 관리자 — 수정은 등급 2 면 누구나
}

// ── 인수인계 대시보드: 오늘의 세정팀 현황 ──
export interface TeamToday {
  team: string; badges: CalendarBadge[];
  /** 소속 본부. 빈 문자열이면 '본부 미지정' 묶음. */
  division: string;
  /** 교대 생산팀(주/야 예측 대상)이면 true. */
  production: boolean;
  /** 묶음 제목으로 쓰는 부서. 빈 문자열이면 부서 없이 맨 앞에 나오는 줄. */
  dept?: string;
  /** 이 줄(팀)의 재직 인원. */
  members?: number;
}
export interface UpcomingEdu {
  memberName: string; courseName: string;
  startDate: string | null; endDate: string | null; eduMethod: string;
}
/** 재직 인원을 생산직/사무직으로 나눈 수 */
export interface Headcount { production: number; office: number; }
export interface TodayStatus {
  date: string;
  teams: TeamToday[];
  upcomingEvents: TeamEvent[];
  upcomingEdu: UpcomingEdu[];
  headcount: Headcount;
}

// ── 포탈 ──
export interface PortalItem {
  id: number;
  groupId: number;
  title: string;
  path: string;
  type: string;
  sortOrder: number;
}
export interface PortalGroup {
  id: number;
  name: string;
  sortOrder: number;
  items: PortalItem[];
}

// ── 자재물류 일정 현황 ──
export interface MaterialVehicle { key: string; label: string; }
export interface MaterialCell { destination: string; vehicles: string[]; }
export interface MaterialRow { person: string; am: MaterialCell; pm: MaterialCell; }
export interface MaterialDay {
  date: string;
  roster: string[];
  vehicles: MaterialVehicle[];
  rows: MaterialRow[];
  noteAm: string;
  notePm: string;
  /** 그날 일정의 버전 — 저장할 때 그대로 돌려보낸다(두 사람이 같은 날을 고치면 뒤 사람이 알게) */
  version?: number;
}
export interface MaterialDestination { name: string; address: string; }

// ── 사용자 관리 ──
export type UserFull = UserDto;

// ── 조직도(부서·팀) ──
export interface OrgMember { id: number; realName: string; rank: string; jobTitle: string; }
export interface OrgTeam {
  name: string; registered: boolean; members: OrgMember[];
  shiftGroup: number;      // 0=교대 없음 / 1조 / 2조 — 근무 예측이 이름 대신 보는 값
  legacyNames: string;     // WPF 에서 쓰던 옛 이름(쉼표 구분). 임포트 시 현재 이름으로 변환
  /** 생산팀 여부(교대조와 별개 축) — 근무표 표시·생산직 집계를 가른다.
   *  교대조가 지정된 팀은 정의상 생산팀이라 항상 true 로 내려온다. */
  isProduction: boolean;
  /** 대시보드 '오늘의 근무 현황' 에 띄울지 */
  showOnDashboard: boolean;
}
export interface OrgDept {
  name: string; registered: boolean; teams: OrgTeam[]; id: number; color: string; shortName: string;
  /** 소속 본부(사업본부). 지정하지 않았으면 빈 문자열. */
  division: string;
  /** 대시보드 '오늘의 근무 현황' 에 띄울지 */
  showOnDashboard: boolean;
  /** 일정 달력의 부서 목록에 띄울지 */
  showOnCalendar: boolean;
  /** 부서별 자료(업체·견적서·체크시트 등)를 따로 쓰는 부서 — 켠 부서만 부서 칩·등록 부서 고르기에 나온다 */
  usesDeptData: boolean;
}
/** 조직도 전체 — 본부 > 부서 > 팀 > 인원. divisions 에는 소속 부서가 아직 없는 본부도 들어간다. */
export interface OrgTree { divisions: string[]; depts: OrgDept[]; }

// ── 현장 점검: 온·습도 모니터링 (Zigbee) ──
/** 상태는 서버가 판정해서 내려준다 — 화면과 알림이 같은 기준을 쓰게 하기 위해서다. */
export type SensorStatusCode = 'normal' | 'warn' | 'alert' | 'offline';

export interface SensorReading {
  deviceId: string; deviceName: string; site: string;
  temperature: number | null; humidity: number | null;
  battery: number | null; linkQuality: number | null;
  receivedAt: string | null;
  status: SensorStatusCode; statusLabel: string; statusReason: string | null;
  batteryLow: boolean;
  /** 이 센서에 걸린 판정 기준이 어디서 왔는지 — '센서 지정' · '동탄 기준' · '전체 기본' … */
  limitSource: string;
}

export interface ZigbeeStatus {
  /** 포털이 Mosquitto 에 붙어 있는지 */
  mqttOnline: boolean;
  /** Zigbee2MQTT 가 살아 있는지. bridge/state 를 아직 못 받았으면 null */
  zigbee2MqttOnline: boolean | null;
  sensorsOnline: number; sensorsTotal: number; message: string | null;
}

export interface SensorSnapshot { sensors: SensorReading[]; status: ZigbeeStatus }

export interface SensorHistoryPoint { receivedAt: string; temperature: number | null; humidity: number | null }
export interface SensorHistory {
  deviceId: string; deviceName: string; points: SensorHistoryPoint[];
  from: string; to: string;
  /** 여러 줄을 묶어 평균 낸 간격(분). 0 이면 원본 그대로 */
  bucketMinutes: number;
  /** 구간이 길어 주기 기록을 빼고 실제 수신만 읽었는지 */
  realOnly: boolean;
}

/** 구간 요약. 기준을 벗어난 시간은 줄 수로 환산한 근사치다. */
export interface SensorSummary {
  deviceId: string; deviceName: string; site: string; limitSource: string;
  count: number; firstAt: string | null; lastAt: string | null;
  tempMin: number | null; tempMax: number | null; tempAvg: number | null;
  humidMin: number | null; humidMax: number | null; humidAvg: number | null;
  normalMinutes: number; warnMinutes: number; alertMinutes: number;
}
export interface SensorSummaryPage { from: string; to: string; sensors: SensorSummary[] }

export interface SensorExportRow {
  receivedAt: string; deviceId: string; deviceName: string; site: string;
  temperature: number | null; humidity: number | null;
  battery: number | null; linkQuality: number | null;
  isSnapshot: boolean; statusLabel: string;
}
export interface SensorExport {
  from: string; to: string; realOnly: boolean; truncated: boolean; rows: SensorExportRow[];
}

/** 온·습도 판정 기준 한 벌. scope 는 global · site · device */
export interface ZigbeeThreshold {
  scope: 'global' | 'site' | 'device'; scopeKey: string; label: string;
  tempNormalMin: number; tempNormalMax: number; tempWarnMin: number; tempWarnMax: number;
  humidNormalMin: number; humidNormalMax: number; humidWarnMin: number; humidWarnMax: number;
  offlineAfterMinutes: number; lowBatteryPercent: number;
  /** 이력 주기 기록 간격(분). 전체 공통이라 '전체 기본' 에서만 의미가 있다. 0 이면 끈다. */
  snapshotIntervalMinutes: number;
  isStored: boolean; updatedAt: string | null; updatedBy: string | null;
}
export interface ZigbeeScopeOption { key: string; label: string }
export interface ZigbeeThresholdPage {
  default: ZigbeeThreshold; rows: ZigbeeThreshold[];
  sites: ZigbeeScopeOption[]; devices: ZigbeeScopeOption[];
}
export interface ZigbeeThresholdResult { success: boolean; message: string }

// ── 대시보드 요약(/api/dashboard/summary) — 권한이 없는 카드는 null ──
export interface DashAlert { level: 'bad' | 'warn'; text: string; link: string; }
export interface DashChecklist { workDate: string; shift: string; submitted: number; inProgress: number; zones: number; openNg: number; weeklyOverdue: number; weeklyDueToday: number; }
export interface DashHandover { open: number; dueToday: number; dueTomorrow: number; overdue: number; }
export interface DashProdReq { open: number; overdue: number; unread: number; }
export interface DashDispatch { count: number; vendors: string[]; }
export interface DashBroken { thisMonth: number; thisYear: number; officialThisYear: number; }
export interface DashboardSummary {
  alerts: DashAlert[]; checklist: DashChecklist | null;
  /** 기타세정 현황 / 주간세정 현황 — 같은 모양 */
  handover: DashHandover | null; weekly: DashHandover | null;
  prodReq: DashProdReq | null; dispatch: DashDispatch | null; broken: DashBroken | null; at: string;
}

// ── 업무 파일 통합 관리 (api/worklog) ──
export interface WorkEquipment { id: number; code: string; line: string; kind: string; process: string; sortOrder: number; isActive: boolean; }
export interface ChemicalCell { date: string; eqCode: string; content: string; note: string; updatedBy: string; updatedAt: string; }
export interface ChemicalMonth { year: number; month: number; equipment: WorkEquipment[]; cells: ChemicalCell[]; }
export interface ChemicalImportResult { added: number; updated: number; skipped: number; newEquipment: string[]; }
export interface WorkReportRow {
  line: string; kind: string; code: string; process: string; content: string; note: string;
  lastChangeDate: string | null; lastChangeContent: string;
}
export interface WorkReport { date: string; rows: WorkReportRow[]; changedCount: number; bake?: BakeLog[]; }

// ── Daily 업무 보고(각 메뉴 기록 모음) — 볼 수 없는 메뉴의 섹션은 null ──
export interface DailyCrewTeam {
  team: string; dept: string; production: boolean; hasShift: boolean; shift: string;
  members: { name: string; tenureMonths: number | null }[];
  day: string[]; night: string[]; off: string[]; edu: string[];
}
export interface DailyCheckZone {
  line: string; code: string; name: string;
  dayState: string; dayNg: number; dayBy: string; nightState: string; nightNg: number; nightBy: string; weeklyOverdue: number;
}
export interface DailyCheckNg { zoneName: string; shift: string; itemText: string; memo: string; checkedBy: string; ngStatus: string; ngCloseNote: string; }
export interface DailyMeeting { title: string; dayContent: string; nightContent: string; officeMemo: string; creatorName: string; }
export interface DailyHandoverItem { vendor: string; content: string; owner: string; inDate: string | null; outDate: string | null; status: string; }
export interface DailyHandover { in: DailyHandoverItem[]; out: DailyHandoverItem[]; overdue: DailyHandoverItem[]; open: number; }
export interface DailyProdReqItem {
  category: string; location: string; requestDetail: string; requester: string;
  dueDate: string | null; status: string; actionDetail: string; assignee: string;
}
export interface DailyBoardBlock { start: string; end: string; nextDay: boolean; recipe: string; minutes: number; }
export interface DailyBoardEq { name: string; group: string; process: string; isIdle: boolean; blocks: DailyBoardBlock[]; }
export interface DailyReport {
  date: string;
  crew: DailyCrewTeam[] | null;
  checklist: { zones: DailyCheckZone[]; ngs: DailyCheckNg[]; openNgAll: number } | null;
  meetings: DailyMeeting[] | null;
  handover: DailyHandover | null; weekly: DailyHandover | null;
  prodReq: { new: DailyProdReqItem[]; done: DailyProdReqItem[]; overdue: DailyProdReqItem[] } | null;
  board: { equipment: DailyBoardEq[]; totalEquipment: number; idleEquipment: number } | null;
  chemical: WorkReport | null;
  waste: WasteMonth | null;
  bake: BakeLog[] | null;
}
export interface WasteLog {
  date: string; shift: string;
  causticBefore: number | null; causticAfter: number | null; causticUsed: number | null;
  wasteBefore: number | null; wasteAfter: number | null; wasteIncrease: number | null;
  dipEquipment: string; sprayEquipment: string; dailyChange: number | null; note: string; updatedBy: string;
}
export interface WasteMonth {
  year: number; month: number; rows: WasteLog[];
  prevCausticAfter: number | null; prevWasteAfter: number | null;
  /** 날짜(yyyy-MM-dd) → 그날 약액을 교체한 설비 */
  chemicalByDate: Record<string, string[]>;
}
/** 월별 추이 — 사용/보충(KOH), 발생(증가)/수거(폐액)를 따로 센다. */
export interface WasteTrendPoint { year: number; month: number; causticUsed: number; wasteIncrease: number; days: number; changes: number; causticRefill: number; wasteRemoved: number; }
/** BAKE OVEN 그을음 한 칸(날짜·교대·회차·오븐). status 가 빈 값이면 가동. */
export interface BakeLog {
  date: string; shift: string; round: number; eqCode: string; status: string;
  trackIn: string | null; trackOut: string | null; item: string; serialNo: string;
  soot: string; tempUp: string; tempDown: string; tempDown2: string; quartz: string; note: string;
  hasSoot: boolean; hasQuartz: boolean; updatedBy: string;
}
export type BakeSave = Omit<BakeLog, 'hasSoot' | 'hasQuartz' | 'updatedBy'>;
export interface BakeDay { date: string; ovens: WorkEquipment[]; rows: BakeLog[]; prevRows: BakeLog[]; }
export interface BakeSearch { rows: BakeLog[]; total: number; }
export interface BakeImportResult { added: number; updated: number; skipped: number; newEquipment: string[]; from: string | null; to: string | null; }
/** 폐기품 LIST(상차 한 번) */
export interface ScrapBatchSummary { id: number; date: string; title: string; isClosed: boolean; count: number; matched: number; loaded: number; }
export interface ScrapItem {
  id: number; line: string; matId: string; matDesc: string; serialNo: string; outNo: string;
  matched: boolean; loaded: boolean; remark: string; sortOrder: number; updatedBy: string;
}
export type ScrapItemSave = Omit<ScrapItem, 'id' | 'sortOrder' | 'updatedBy'>;
export interface ScrapBatch {
  id: number; date: string; title: string; note: string; isClosed: boolean; closedAt: string | null; closedBy: string;
  createdBy: string; items: ScrapItem[];
}
export interface ScrapTag {
  id: number; date: string; writer: string; item: string; serialNo: string; line: string; circle: string;
  owner: string; note: string; done: boolean; updatedBy: string;
}
export type ScrapTagSave = Omit<ScrapTag, 'id' | 'updatedBy'>;
export interface ScrapCircle { id: number; name: string; line: string; owner: string; }
export interface ScrapMaterial { matId: string; matDesc: string; }
export interface ScrapSearch { items: { batchId: number; batchDate: string; batchClosed: boolean; item: ScrapItem }[]; tags: ScrapTag[]; }
export interface ScrapImportResult { batches: number; items: number; skippedBatches: number; tags: number; skippedTags: number; circles: number; }
/** 양식 다운로드 한 줄 — fileRef 는 att:번호|이름|종류 */
export interface WorkForm { id: number; no: string; title: string; description: string; fileRef: string; updatedBy: string; updatedAt: string; }
