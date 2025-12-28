namespace Bloomie.Authorization
{
    /// <summary>
    /// Ma trận phân quyền cho hệ thống Bloomie
    /// Quy định ai có thể làm gì với ai
    /// </summary>
    public static class PermissionMatrix
    {
        /// <summary>
        /// Quyền quản lý người dùng (xem, sửa, xóa, phân quyền)
        /// </summary>
        public static class UserManagement
        {
            /// <summary>
            /// Kiểm tra người dùng có quyền xem/sửa người dùng khác không
            /// </summary>
            /// <param name="managerRole">Role của người quản lý (Admin/Manager/Staff)</param>
            /// <param name="targetRole">Role của người bị quản lý</param>
            /// <param name="isSuperAdmin">Người quản lý có phải Super Admin không</param>
            /// <param name="isTargetSuperAdmin">Người bị quản lý có phải Super Admin không</param>
            /// <returns>true nếu có quyền, false nếu không</returns>
            public static bool CanManage(string managerRole, string targetRole, 
                                        bool isSuperAdmin = false, 
                                        bool isTargetSuperAdmin = false)
            {
                // 🔒 RULE 1: Super Admin KHÔNG THỂ bị ai quản lý (trừ chính mình)
                if (isTargetSuperAdmin && !isSuperAdmin)
                {
                    return false;
                }
                
                // ⭐ RULE 2: Super Admin có thể quản lý MỌI NGƯỜI (kể cả Admin khác)
                if (isSuperAdmin)
                {
                    return true;
                }
                
                // 🎯 RULE 3: Admin thường chỉ quản lý Manager, Staff, Shipper, User (KHÔNG quản lý Admin khác)
                if (managerRole == "Admin")
                {
                    return targetRole is "Manager" or "Staff" or "Shipper" or "User";
                }
                
                // 👔 RULE 4: Manager quản lý Staff, Shipper và User
                if (managerRole == "Manager")
                {
                    return targetRole is "Staff" or "Shipper" or "User";
                }
                
                // 👷 RULE 5: Staff chỉ XEM User (không sửa/xóa)
                if (managerRole == "Staff")
                {
                    return targetRole == "User";
                }
                
                return false;
            }
            
            /// <summary>
            /// Kiểm tra có quyền nâng cấp role cho user không
            /// </summary>
            public static bool CanPromoteToRole(string currentUserRole, string targetRole, bool isSuperAdmin = false)
            {
                // Super Admin có thể gán BẤT KỲ role nào
                if (isSuperAdmin)
                {
                    return true;
                }
                
                return currentUserRole switch
                {
                    "Admin" => targetRole is "Manager" or "Staff" or "Shipper" or "User",
                    "Manager" => targetRole is "Staff" or "Shipper" or "User",
                    _ => false
                };
            }
            
            /// <summary>
            /// Kiểm tra có quyền xóa user không
            /// </summary>
            public static bool CanDelete(string managerRole, string targetRole, 
                                        bool isSuperAdmin = false, 
                                        bool isTargetSuperAdmin = false)
            {
                // 🔒 RULE 1: KHÔNG THỂ xóa Super Admin
                if (isTargetSuperAdmin)
                {
                    return false;
                }
                
                // ⭐ RULE 2: Super Admin có thể xóa Admin thường, Manager, Staff, Shipper, User
                if (isSuperAdmin)
                {
                    return targetRole is "Admin" or "Manager" or "Staff" or "Shipper" or "User";
                }
                
                // 🎯 RULE 3: Admin thường có thể xóa Manager, Staff, Shipper, User (KHÔNG xóa Admin khác)
                if (managerRole == "Admin")
                {
                    return targetRole is "Manager" or "Staff" or "Shipper" or "User";
                }
                
                // 👔 RULE 4: Manager có thể xóa Staff, Shipper và User
                if (managerRole == "Manager")
                {
                    return targetRole is "Staff" or "Shipper" or "User";
                }
                
                // 👷 RULE 5: Staff KHÔNG có quyền xóa ai
                return false;
            }
            
            /// <summary>
            /// Kiểm tra có quyền khóa/mở khóa user không
            /// </summary>
            public static bool CanLockUnlock(string managerRole, string targetRole, 
                                            bool isSuperAdmin = false, 
                                            bool isTargetSuperAdmin = false)
            {
                // Quy tắc giống như Delete
                return CanDelete(managerRole, targetRole, isSuperAdmin, isTargetSuperAdmin);
            }
        }
        
        /// <summary>
        /// Quyền giám sát hoạt động
        /// </summary>
        public static class Monitoring
        {
            /// <summary>
            /// Kiểm tra có quyền xem hoạt động của user không
            /// </summary>
            public static bool CanViewActivity(string viewerRole, string targetRole, bool isSuperAdmin = false)
            {
                // Super Admin xem tất cả
                if (isSuperAdmin)
                {
                    return true;
                }
                
                return viewerRole switch
                {
                    "Admin" => true, // Admin xem tất cả (trừ Super Admin)
                    "Manager" => targetRole is "Staff" or "Shipper" or "User",
                    "Staff" => targetRole == "User",
                    _ => false
                };
            }
            
            /// <summary>
            /// Kiểm tra có quyền truy cập Dashboard không
            /// </summary>
            public static bool CanAccessDashboard(string role)
            {
                return role is "Admin" or "Manager" or "Staff" or "Shipper";
            }
        }
    }
}
