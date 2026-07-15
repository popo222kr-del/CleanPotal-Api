// API DTO와 매칭되는 타입 (camelCase JSON)

export interface UserDto {
  id: number;
  username: string;
  realName: string;
  teamName: string;
  jobTitle: string;
  isAdmin: boolean;
  canManageFiles: boolean;
  canManageNotices: boolean;
  canManageVendors: boolean;
  canManageSchedule: boolean;
  canManageBroken: boolean;
  canAccessEtcMenu: boolean;
  canManageShiftBoard: boolean;
  canManageInventory: boolean;
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
export interface TeamEvent {
  id: number;
  registeredBy: string;
  startDate: string;
  endDate: string;
  content: string;
  detail: string;
  createDate: string;
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
}

// ── 업체 관리 ──
export interface Vendor {
  id: number; vendorName: string; category: string; isWeekly: boolean; isFavorite: boolean;
  basePath: string; addresses: string; managers: string;
  contact: string; phone: string; note: string;
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

// ── 견적서 (실제 quotations.json 구조) ──
export interface QuotationItem {
  id: number; no: number; description: string; partCode: string; standardSpec: string;
  listPrice: number; qty: number; amount: number;
}
export interface Quotation {
  id: number; quoteNo: string; rfqNo: string; company: string; attention: string;
  email: string; phone: string; quoteDate: string | null; validity: string;
  aetsManager: string; aetsPhone: string; businessNo: string;
  remarks: string; memo: string; sourceFileName: string;
  createdBy: string; createdAt: string; lastModifiedBy: string; lastModifiedAt: string | null;
  total: number; items: QuotationItem[];
}
export interface QuotationSummary {
  id: number; quoteNo: string; rfqNo: string; company: string; quoteDate: string | null;
  validity: string; total: number; itemCount: number; aetsManager: string; createdAt: string;
}

// ── 견적 마스터 (단가표·템플릿·설정) ──
export interface ProductMaster {
  id: number; productName: string; partCode: string; spec: string; unitPrice: number;
  vendorName: string; unit: string; updatedBy: string; updatedAt: string;
}
export interface GlobalTemplate { id: number; productCode: string; productName: string; templatePath: string; }
export interface QuotationConfig { businessNo: string; }

// ── 세정 레시피 ──
export interface Recipe {
  id: number; text: string; displayText: string;
  s2Minutes: number; s2Temperature: number; hfMinutes: number; diMinutes: number; totalMinutes: number;
  isFavorite: boolean; orderIndex: number;
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

// ── 체크시트 ──
export interface Zone { key: string; label: string; group: string; }
export interface InspectionItem { id: number; zone: string; sortOrder: number; text: string; }
export interface InspectionRecord {
  id: number; zone: string; date: string; shift: string; worker: string;
  checkedCount: number; totalCount: number; submittedAt: string;
}

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
}
export interface ReportSummary { id: number; title: string; shortTitle: string; dateRange: string; blockCount: number; hasMemo: boolean; }
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
}

// ── 개인별 업무 분장표 ──
export interface WorkMember { id: number; username: string; realName: string; teamName: string; jobTitle: string; isHidden: boolean; resignDate: string; }
export interface WorkAccount { id: number; username: string; serviceName: string; accountId: string; accountPassword: string; note: string; }
export interface WorkEdu { id: number; username: string; eduName: string; eduDate: string; instructor: string; note: string; startDate: string; endDate: string; }
export interface WorkMemberDetail { member: WorkMember; accounts: WorkAccount[]; edus: WorkEdu[]; }

// ── 배차 ──
export interface Dispatch {
  id: number; vendorName: string; outgoingDetails: string; incomingDetails: string;
  managerName: string; contactNumber: string; fullAddress: string; note: string; createDate: string;
}

// ── 사무실 공지 ──
export interface Notice { id: number; title: string; content: string; author: string; createdAt: string; }

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
}

// ── 인수인계 대시보드: 오늘의 세정팀 현황 ──
export interface TeamToday { team: string; badges: CalendarBadge[]; }
export interface UpcomingEdu {
  memberName: string; courseName: string;
  startDate: string | null; endDate: string | null; eduMethod: string;
}
export interface TodayStatus {
  date: string;
  teams: TeamToday[];
  upcomingEvents: TeamEvent[];
  upcomingEdu: UpcomingEdu[];
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
}
export interface MaterialDestination { name: string; address: string; }

// ── 사용자 관리 (전체 필드) ──
export interface UserFull {
  id: number;
  username: string;
  realName: string;
  teamName: string;
  jobTitle: string;
  email: string;
  phoneNumber: string;
  employeeNumber: string;
  hireDate: string;
  isResigned: boolean;
  resignDate: string;
  isAdmin: boolean;
  canManageFiles: boolean;
  canManageNotices: boolean;
  canManageVendors: boolean;
  canManageSchedule: boolean;
  canManageBroken: boolean;
  canAccessEtcMenu: boolean;
  canManageShiftBoard: boolean;
  canManageInventory: boolean;
}
