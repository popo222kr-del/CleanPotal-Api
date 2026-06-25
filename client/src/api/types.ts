// API DTO와 매칭되는 타입 (camelCase JSON)

export interface UserDto {
  id: number;
  username: string;
  realName: string;
  teamName: string;
  jobTitle: string;
  isAdmin: boolean;
  canManageFiles: boolean;
  canManageSchedule: boolean;
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

// ── 현장 재고 ──
export interface InventoryItem {
  id: number; itemCode: string; itemName: string; category: string; unit: string;
  storageLocation: string; currentStock: number; appropriateStock: number; previousStock: number;
  orderDate: string | null; expectedDate: string | null; note: string; needsOrder: boolean; updatedAt: string;
}
export interface InventoryZone { location: string; items: InventoryItem[]; }

// ── 견적서 ──
export interface QuotationItem {
  id: number; sortOrder: number; itemName: string; spec: string; unit: string;
  quantity: number; unitPrice: number; amount: number; remarks: string;
}
export interface Quotation {
  id: number; quoteNo: string; vendorName: string; quoteDate: string | null;
  validUntil: string | null; status: string; remarks: string; createdBy: string;
  createdAt: string; updatedAt: string | null; total: number; items: QuotationItem[];
}
export interface QuotationSummary {
  id: number; quoteNo: string; vendorName: string; quoteDate: string | null;
  validUntil: string | null; status: string; total: number; itemCount: number; createdBy: string;
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

// ── 생산미팅 ──
export interface ProductionMeeting {
  id: number;
  title: string;
  meetingDate: string;
  dayContent: string;
  nightContent: string;
  officeMemo: string;
  dayTeam: string;
  nightTeam: string;
  creatorName: string;
  createdAt: string;
  updatedAt: string | null;
}
export interface ProductionMeetingGroup {
  monthTitle: string;
  reports: ProductionMeeting[];
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
  canAccessEtcMenu: boolean;
}
