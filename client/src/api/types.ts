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
export interface CalendarDay {
  date: string;
  day: number;
  dayOfWeek: string;
  isWeekend: boolean;
  holiday: string;
  dayShift: string[];
  nightShift: string[];
  offShift: string[];
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
}

// ── 업체 관리 ──
export interface Vendor {
  id: number; vendorName: string; category: string; isWeekly: boolean;
  contact: string; phone: string; note: string;
}

// ── 현장 재고 ──
export interface InventoryItem {
  id: number; itemCode: string; itemName: string; category: string; unit: string;
  storageLocation: string; currentStock: number; appropriateStock: number; previousStock: number;
  orderDate: string | null; expectedDate: string | null; note: string; needsOrder: boolean; updatedAt: string;
}
export interface InventoryZone { location: string; items: InventoryItem[]; }

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
  createdAt: string;
}
export interface BrokenFilterOptions {
  years: number[];
  teams: string[];
  productTypes: string[];
}

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
export interface ReportSummary { id: number; title: string; shortTitle: string; dateRange: string; blockCount: number; }
export interface ReportGroup { monthTitle: string; reports: ReportSummary[]; }

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
  progressPercent: number;
  creatorName: string;
  createDate: string;
  modifierName: string;
  modifyDate: string | null;
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
