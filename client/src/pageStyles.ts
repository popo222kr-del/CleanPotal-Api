// 모든 화면 CSS 를 첫 로딩에 한 번에 받는다(화면 JS 는 여전히 열 때 받는다 — App.tsx lazyPage).
// .pg-header / .pg-body / .modal-* 같은 공용 클래스가 각 화면 CSS 에 흩어져 있어서,
// 화면별로 CSS 를 따로 받으면 바로 들어간 화면에선 그 스타일이 빠진다(2026-09 지연 로딩 회귀).
// 순서가 곧 우선순위다 — 예전 한 번에 묶던 순서(App.tsx import 순서) 그대로 둔다. 새 화면 CSS 는 끝에 추가.
import './pages/mes/Mes.css';
import './components/Layout.css';
import './pages/Login.css';
import './pages/Roster.css';
import './pages/Material.css';
import './pages/Calendar.css';
import './pages/Mes.css';
import './pages/mes/shell/MesShell.css';
import './pages/Handover.css';
import './pages/ProdReq.css';
import './pages/ProdReqOptions.css';
import './pages/Weekly.css';
import './pages/Meeting.css';
import './pages/ScheduleBoard.css';
import './pages/checklist/Checklist.css';
import './components/Combo.css';
import './pages/Broken.css';
import './pages/Quotation.css';
import './pages/ProductMaster.css';
import './pages/Notice.css';
import './pages/Dispatch.css';
import './pages/EduDashboard.css';
import './styles/member-list.css';
import './pages/WorkAssignment.css';
import './pages/TempHumidity.css';
import './pages/Inventory.css';
import './pages/Icpms.css';
import './pages/Vendors.css';
import './pages/Portal.css';
import './pages/Dashboard.css';
import './pages/Users.css';
import './pages/Holidays.css';
