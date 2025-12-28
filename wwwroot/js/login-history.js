// Hàm lấy vị trí từ IP
async function getLocationFromIP(ipAddress, element) {
    try {
        const response = await fetch(`https://ipapi.co/${ipAddress}/json/`);
        const data = await response.json();
        if (data.city && data.country_name) {
            element.textContent = `${data.city}, ${data.country_name}`;
        } else {
            element.textContent = "Không xác định";
        }
    } catch (error) {
        element.textContent = "Không xác định";
        console.error("Lỗi khi lấy thông tin vị trí:", error);
    }
}

// Hàm đăng xuất khỏi thiết bị
async function logoutDevice(sessionId) {
    if (!confirm("Bạn có chắc muốn đăng xuất khỏi thiết bị này?")) {
        return;
    }

    try {
        const response = await fetch("/Account/LogoutDevice", {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "RequestVerificationToken": document.querySelector('input[name="__RequestVerificationToken"]').value
            },
            body: JSON.stringify({ sessionId: sessionId })
        });

        if (response.ok) {
            // Refresh trang sau khi đăng xuất thành công
            window.location.reload();
        } else {
            alert("Có lỗi xảy ra khi đăng xuất thiết bị. Vui lòng thử lại.");
        }
    } catch (error) {
        console.error("Lỗi khi đăng xuất thiết bị:", error);
        alert("Có lỗi xảy ra khi đăng xuất thiết bị. Vui lòng thử lại.");
    }
}

// Khởi tạo khi trang load
document.addEventListener("DOMContentLoaded", function() {
    // Lấy thông tin vị trí cho mỗi lịch sử đăng nhập
    document.querySelectorAll(".login-history-item").forEach(item => {
        const ipAddress = item.querySelector(".text-muted small:last-child").textContent.match(/IP: ([\d\.]+)/)[1];
        const locationElement = item.querySelector(".location-text");
        if (ipAddress && locationElement) {
            getLocationFromIP(ipAddress, locationElement);
        }
    });
});