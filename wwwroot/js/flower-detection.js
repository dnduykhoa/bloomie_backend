// Flower Detection với Camera/Upload
let flowerDetectionStream = null;
let flowerDetectionTimer = null;
let isFlowerDetectionProcessing = false;
let isRealTimeMode = false;
let capturedImagePath = null;
let detectionResult = null;
let selectedPackaging = 'basic';
const packagingFees = {
    'basic': 100000,
    'premium': 150000
};

// Format currency
function formatCurrency(amount) {
    return new Intl.NumberFormat('vi-VN', {
        style: 'currency',
        currency: 'VND'
    }).format(amount);
}

// Open Flower Detection Modal
function openFlowerDetectionModal() {
    const modal = document.getElementById('flowerDetectionModal');
    modal.style.display = 'flex';
    initFlowerCamera();
}

// Close Flower Detection Modal
function closeFlowerDetectionModal() {
    const modal = document.getElementById('flowerDetectionModal');
    modal.style.display = 'none';
    
    // Stop camera
    if (flowerDetectionStream) {
        flowerDetectionStream.getTracks().forEach(track => track.stop());
        flowerDetectionStream = null;
    }
    
    // Clear timers
    if (flowerDetectionTimer) {
        clearInterval(flowerDetectionTimer);
        flowerDetectionTimer = null;
    }
    stopVideoDetection();
    
    // Stop and clear uploaded video
    const uploadedVideo = document.getElementById('flowerUploadedVideo');
    if (uploadedVideo) {
        uploadedVideo.pause();
        uploadedVideo.src = '';
        uploadedVideo.style.display = 'none';
    }
    
    // Reset state
    isRealTimeMode = false;
    isFlowerDetectionProcessing = false;
    capturedImagePath = null;
    detectionResult = null;
    selectedPackaging = 'basic';
    
    // Clear UI
    document.getElementById('flowerDetectionResult').innerHTML = '';
    document.getElementById('flowerCapturedImage').style.display = 'none';
    document.getElementById('flowerVideo').style.display = 'block';
    
    const captureBtn = document.getElementById('flowerVideoCaptureBtn');
    if (captureBtn) captureBtn.style.display = 'none';
    
    // Hiện lại action buttons
    const actionsDiv = document.getElementById('flowerActions');
    if (actionsDiv) {
        actionsDiv.style.display = 'block';
    }
}

// Init Camera
async function initFlowerCamera() {
    try {
        const video = document.getElementById('flowerVideo');
        flowerDetectionStream = await navigator.mediaDevices.getUserMedia({ video: true });
        video.srcObject = flowerDetectionStream;
    } catch (error) {
        console.error('Error accessing camera:', error);
        alert('Không thể truy cập camera. Vui lòng kiểm tra quyền truy cập.');
    }
}

// Toggle Real-time Mode
function toggleFlowerRealTimeMode() {
    isRealTimeMode = !isRealTimeMode;
    
    const button = document.getElementById('flowerLiveScanBtn');
    const actionsDiv = document.getElementById('flowerActions');
    const liveIndicator = document.getElementById('flowerLiveIndicator');
    const stopLiveBtn = document.getElementById('flowerStopLiveBtn');
    
    if (isRealTimeMode) {
        button.textContent = '⏹ Dừng';
        button.style.backgroundColor = '#dc3545';
        
        // Clear previous result
        detectionResult = null;
        capturedImagePath = null;
        document.getElementById('flowerDetectionResult').innerHTML = '<p style="color: white;">Đang quét liên tục...</p>';
        
        // Ẩn các nút khác khi bật live mode
        if (actionsDiv) {
            actionsDiv.style.display = 'none';
        }
        
        // Hiện live indicator và nút stop
        if (liveIndicator) {
            liveIndicator.style.display = 'flex';
        }
        if (stopLiveBtn) {
            stopLiveBtn.style.display = 'flex';
        }
        
        // Start periodic capture
        flowerDetectionTimer = setInterval(() => {
            if (!isFlowerDetectionProcessing) {
                captureAndDetectFlower();
            }
        }, 1000); // Every 1 second
    } else {
        button.textContent = '▶ Live';
        button.style.backgroundColor = '#C41E3A';
        
        // Stop timer
        if (flowerDetectionTimer) {
            clearInterval(flowerDetectionTimer);
            flowerDetectionTimer = null;
        }
        
        // Reset detection result
        detectionResult = null;
        
        // Hiện lại action buttons khi tắt live mode
        if (actionsDiv) {
            actionsDiv.style.display = 'block';
        }
        
        // Ẩn live indicator và nút stop
        if (liveIndicator) {
            liveIndicator.style.display = 'none';
        }
        if (stopLiveBtn) {
            stopLiveBtn.style.display = 'none';
        }
        
        // Xóa kết quả overlay
        document.getElementById('flowerDetectionResult').innerHTML = '';
    }
}

// Capture and Detect (for Real-time mode)
async function captureAndDetectFlower() {
    if (isFlowerDetectionProcessing) return;
    
    isFlowerDetectionProcessing = true;
    
    try {
        const video = document.getElementById('flowerVideo');
        const canvas = document.createElement('canvas');
        canvas.width = video.videoWidth;
        canvas.height = video.videoHeight;
        const ctx = canvas.getContext('2d');
        ctx.drawImage(video, 0, 0);
        
        // Convert to blob
        const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', 0.85));
        
        // Send to API
        await processFlowerImage(blob);
    } catch (error) {
        console.error('Error capturing:', error);
    } finally {
        isFlowerDetectionProcessing = false;
    }
}

// Reset to initial state (show action buttons)
function resetFlowerDetection() {
    const video = document.getElementById('flowerVideo');
    const uploadedVideo = document.getElementById('flowerUploadedVideo');
    const capturedImage = document.getElementById('flowerCapturedImage');
    const actionsDiv = document.getElementById('flowerActions');
    const resultDiv = document.getElementById('flowerDetectionResult');
    const captureBtn = document.getElementById('flowerVideoCaptureBtn');
    const liveIndicator = document.getElementById('flowerLiveIndicator');
    
    // Stop video detection if running
    stopVideoDetection();
    
    // Stop uploaded video
    if (uploadedVideo) {
        uploadedVideo.pause();
        uploadedVideo.src = '';
        uploadedVideo.style.display = 'none';
    }
    
    // Show camera video, hide others
    video.style.display = 'block';
    capturedImage.style.display = 'none';
    if (captureBtn) captureBtn.style.display = 'none';
    if (liveIndicator) liveIndicator.style.display = 'none';
    
    // Clear results
    resultDiv.innerHTML = '';
    detectionResult = null;
    
    // Show action buttons
    if (actionsDiv) {
        actionsDiv.style.display = 'block';
    }
    
    // Reset processing flag
    isFlowerDetectionProcessing = false;
}

// Capture Once (for snapshot)
async function captureFlowerOnce() {
    if (isFlowerDetectionProcessing) return;
    
    // Stop live mode if active
    if (isRealTimeMode) {
        toggleFlowerRealTimeMode();
    }
    
    const video = document.getElementById('flowerVideo');
    const capturedImage = document.getElementById('flowerCapturedImage');
    
    // If there's a previous capture, reset UI first
    if (capturedImage.style.display === 'block') {
        video.style.display = 'block';
        capturedImage.style.display = 'none';
        document.getElementById('flowerDetectionResult').innerHTML = '';
        detectionResult = null;
        
        // Wait a bit for video to be visible again
        await new Promise(resolve => setTimeout(resolve, 100));
    }
    
    isFlowerDetectionProcessing = true;
    
    try {
        const canvas = document.createElement('canvas');
        canvas.width = video.videoWidth;
        canvas.height = video.videoHeight;
        const ctx = canvas.getContext('2d');
        ctx.drawImage(video, 0, 0);
        
        // Show captured image
        capturedImage.src = canvas.toDataURL('image/jpeg');
        capturedImage.style.display = 'block';
        video.style.display = 'none';
        
        // Convert to blob
        const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', 0.85));
        
        // Send to API
        await processFlowerImage(blob, true);
    } catch (error) {
        console.error('Error capturing:', error);
        alert('Lỗi khi chụp ảnh. Vui lòng thử lại.');
    } finally {
        isFlowerDetectionProcessing = false;
    }
}

// Process Image (send to API)
async function processFlowerImage(blob, showDetailedResult = false) {
    try {
        const formData = new FormData();
        formData.append('imageFile', blob, 'flower.jpg');
        
        const response = await fetch('/api/FlowerDetectionApi/detect', {
            method: 'POST',
            body: formData
        });
        
        const result = await response.json();
        
        if (result.success) {
            detectionResult = result;
            displayFlowerResult(result, showDetailedResult);
        } else {
            console.error('Detection failed:', result.message);
        }
    } catch (error) {
        console.error('Error processing image:', error);
    }
}

// Display Result
function displayFlowerResult(result, showDetailedResult = false) {
    const resultDiv = document.getElementById('flowerDetectionResult');
    const actionsDiv = document.getElementById('flowerActions');
    
    if (!result.flowersWithPrices || result.flowersWithPrices.length === 0) {
        resultDiv.innerHTML = '<p style="color: white;">Không phát hiện hoa nào</p>';
        return;
    }
    
    // Ẩn action buttons khi có kết quả
    if (actionsDiv) {
        actionsDiv.style.display = 'none';
    }
    
    if (showDetailedResult) {
        // Detailed view (after snapshot)
        resultDiv.innerHTML = `
            <div style="background: rgba(255,255,255,0.95); border-radius: 16px; padding: 20px; max-height: 60vh; overflow-y: auto;">
                <!-- Header -->
                <div style="display: flex; align-items: center; gap: 12px; margin-bottom: 20px; padding: 16px; background: linear-gradient(135deg, rgba(196,30,58,0.1), rgba(196,30,58,0.05)); border-radius: 12px;">
                    <div style="background: #C41E3A; border-radius: 50%; padding: 10px;">
                        <i class="fas fa-check" style="color: white; font-size: 20px;"></i>
                    </div>
                    <div>
                        <h3 style="margin: 0; font-size: 18px;">Kết quả nhận diện</h3>
                        <p style="margin: 0; color: #666; font-size: 14px;">${result.total} bông hoa phát hiện</p>
                    </div>
                </div>
                
                <!-- Flower List -->
                <div style="margin-bottom: 20px;">
                    <h4 style="font-size: 16px; margin-bottom: 12px; display: flex; align-items: center; gap: 8px;">
                        <i class="fas fa-seedling" style="color: #C41E3A;"></i>
                        Chi tiết từng loại
                    </h4>
                    ${result.flowersWithPrices.map(flower => `
                        <div style="background: white; border: 1px solid #eee; border-radius: 12px; padding: 16px; margin-bottom: 10px; box-shadow: 0 2px 8px rgba(0,0,0,0.05);">
                            <div style="display: flex; align-items: center; gap: 12px;">
                                <div style="background: rgba(196,30,58,0.1); border-radius: 50%; padding: 10px;">
                                    <i class="fas fa-seedling" style="color: #C41E3A; font-size: 20px;"></i>
                                </div>
                                <div style="flex: 1;">
                                    <div style="font-weight: bold; font-size: 15px; margin-bottom: 4px;">${flower.displayName}</div>
                                    <div style="color: #666; font-size: 13px;">${formatCurrency(flower.pricePerStem)}/bông</div>
                                </div>
                                <div style="display: flex; align-items: center; gap: 8px; background: #f5f5f5; border-radius: 8px; padding: 6px;">
                                    <button onclick="decreaseFlowerQuantity('${flower.name}')" style="background: none; border: none; color: #C41E3A; cursor: pointer; font-size: 18px; padding: 4px 8px;">−</button>
                                    <span id="qty-${flower.name}" style="font-weight: bold; min-width: 30px; text-align: center;">${flower.quantity}</span>
                                    <button onclick="increaseFlowerQuantity('${flower.name}')" style="background: none; border: none; color: #C41E3A; cursor: pointer; font-size: 18px; padding: 4px 8px;">+</button>
                                </div>
                                <div id="price-${flower.name}" style="font-weight: bold; color: #C41E3A; font-size: 16px; min-width: 100px; text-align: right;">
                                    ${formatCurrency(flower.totalPrice)}
                                </div>
                            </div>
                        </div>
                    `).join('')}
                </div>
                
                <!-- Packaging Selection -->
                <div style="margin-bottom: 20px;">
                    <h4 style="font-size: 16px; margin-bottom: 12px; display: flex; align-items: center; gap: 8px;">
                        <i class="fas fa-gift" style="color: #C41E3A;"></i>
                        Chọn gói hoa
                    </h4>
                    <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px;">
                        <div onclick="selectPackaging('basic')" id="pkg-basic" style="border: 2px solid #C41E3A; background: rgba(196,30,58,0.1); border-radius: 12px; padding: 16px; cursor: pointer; text-align: center;">
                            <i class="fas fa-box" style="font-size: 28px; color: #C41E3A; margin-bottom: 8px;"></i>
                            <div style="font-weight: bold; margin-bottom: 4px;">Gói cơ bản</div>
                            <div style="font-weight: bold; color: #C41E3A; font-size: 16px; margin-bottom: 4px;">100.000₫</div>
                            <div style="font-size: 12px; color: #666;">Giấy + Nơ</div>
                        </div>
                        <div onclick="selectPackaging('premium')" id="pkg-premium" style="border: 2px solid #ddd; background: white; border-radius: 12px; padding: 16px; cursor: pointer; text-align: center;">
                            <i class="fas fa-crown" style="font-size: 28px; color: #666; margin-bottom: 8px;"></i>
                            <div style="font-weight: bold; margin-bottom: 4px;">Gói cao cấp</div>
                            <div style="font-weight: bold; color: #666; font-size: 16px; margin-bottom: 4px;">150.000₫</div>
                            <div style="font-size: 12px; color: #666;">Thiết kế đẹp</div>
                        </div>
                    </div>
                </div>
                
                <!-- Total -->
                <div style="background: linear-gradient(135deg, #C41E3A, #E63946); border-radius: 12px; padding: 20px; color: white; margin-bottom: 20px;">
                    <div style="display: flex; justify-content: space-between; align-items: center;">
                        <span style="font-weight: bold; font-size: 16px;">Tổng giá trị</span>
                        <span id="totalValue" style="font-weight: bold; font-size: 20px;">${formatCurrency(result.totalValue + packagingFees[selectedPackaging])}</span>
                    </div>
                </div>
                
                <!-- Action Buttons -->
                <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px;">
                    <button onclick="resetFlowerDetection()" style="background: white; color: #C41E3A; border: 2px solid #C41E3A; padding: 14px; border-radius: 12px; font-weight: bold; cursor: pointer; font-size: 15px;">
                        <i class="fas fa-camera"></i> Chụp lại
                    </button>
                    <button onclick="addFlowerBouquetToCart()" style="background: #C41E3A; color: white; border: none; padding: 14px; border-radius: 12px; font-weight: bold; cursor: pointer; font-size: 15px;">
                        <i class="fas fa-shopping-cart"></i> Thêm vào giỏ
                    </button>
                </div>
            </div>
        `;
    } else {
        // Live mode - overlay trực tiếp trên video
        const flowers = result.flowersWithPrices.slice(0, 3);
        resultDiv.innerHTML = `
            <div style="position: absolute; bottom: 120px; left: 20px; right: 20px; z-index: 8; max-height: calc(100vh - 300px); overflow-y: auto;">
                <div style="color: white; font-weight: bold; margin-bottom: 12px; text-shadow: 0 2px 4px rgba(0,0,0,0.8); font-size: 16px;">
                    Chi tiết phát hiện
                </div>
                ${flowers.map(flower => `
                    <div style="background: rgba(40,40,40,0.85); backdrop-filter: blur(10px); border-radius: 12px; padding: 12px; margin-bottom: 10px; border: 1px solid rgba(255,255,255,0.2);">
                        <div style="display: flex; align-items: center; gap: 12px;">
                            <div style="background: rgba(196,30,58,0.9); border-radius: 50%; padding: 8px; flex-shrink: 0;">
                                <i class="fas fa-seedling" style="color: white; font-size: 16px;"></i>
                            </div>
                            <div style="flex: 1; min-width: 0;">
                                <div style="color: white; font-weight: bold; font-size: 14px; text-shadow: 0 1px 3px rgba(0,0,0,0.5); white-space: nowrap; overflow: hidden; text-overflow: ellipsis;">${flower.displayName}</div>
                                <div style="color: rgba(255,255,255,0.9); font-size: 12px; text-shadow: 0 1px 3px rgba(0,0,0,0.5);">${flower.quantity} bông × ${formatCurrency(flower.pricePerStem)}</div>
                            </div>
                            <div style="color: white; font-weight: bold; font-size: 15px; text-shadow: 0 1px 3px rgba(0,0,0,0.5); flex-shrink: 0;">
                                ${formatCurrency(flower.totalPrice)}
                            </div>
                        </div>
                    </div>
                `).join('')}
                <div style="background: rgba(196,30,58,0.95); border-radius: 12px; padding: 14px; color: white; backdrop-filter: blur(10px); box-shadow: 0 4px 15px rgba(196,30,58,0.6); margin-bottom: 12px;">
                    <div style="display: flex; justify-content: space-between; align-items: center;">
                        <span style="font-weight: bold; font-size: 15px;">Tổng giá trị</span>
                        <span style="font-weight: bold; font-size: 18px;">${formatCurrency(result.totalValue)}</span>
                    </div>
                </div>
                <button onclick="captureFlowerOnce()" style="width: 100%; background: white; color: #C41E3A; border: none; padding: 12px; border-radius: 12px; font-weight: bold; cursor: pointer; font-size: 14px; box-shadow: 0 4px 12px rgba(0,0,0,0.3); display: flex; align-items: center; justify-content: center; gap: 8px;">
                    <i class="fas fa-camera"></i> Chụp ảnh ngay
                </button>
            </div>
        `;
    }
}

// Increase/Decrease quantity
function increaseFlowerQuantity(flowerName) {
    if (!detectionResult) return;
    
    const flower = detectionResult.flowersWithPrices.find(f => f.name === flowerName);
    if (flower) {
        flower.quantity++;
        flower.totalPrice = flower.quantity * flower.pricePerStem;
        updateFlowerDisplay(flower);
        updateTotalValue();
    }
}

function decreaseFlowerQuantity(flowerName) {
    if (!detectionResult) return;
    
    const flower = detectionResult.flowersWithPrices.find(f => f.name === flowerName);
    if (flower && flower.quantity > 1) {
        flower.quantity--;
        flower.totalPrice = flower.quantity * flower.pricePerStem;
        updateFlowerDisplay(flower);
        updateTotalValue();
    }
}

function updateFlowerDisplay(flower) {
    const qtyElement = document.getElementById(`qty-${flower.name}`);
    const priceElement = document.getElementById(`price-${flower.name}`);
    
    if (qtyElement) qtyElement.textContent = flower.quantity;
    if (priceElement) priceElement.textContent = formatCurrency(flower.totalPrice);
}

function updateTotalValue() {
    if (!detectionResult) return;
    
    const totalValue = detectionResult.flowersWithPrices.reduce((sum, f) => sum + f.totalPrice, 0);
    const totalElement = document.getElementById('totalValue');
    
    if (totalElement) {
        totalElement.textContent = formatCurrency(totalValue + packagingFees[selectedPackaging]);
    }
}

// Select Packaging
function selectPackaging(type) {
    selectedPackaging = type;
    
    // Update UI
    const basicPkg = document.getElementById('pkg-basic');
    const premiumPkg = document.getElementById('pkg-premium');
    
    if (type === 'basic') {
        basicPkg.style.border = '2px solid #C41E3A';
        basicPkg.style.background = 'rgba(196,30,58,0.1)';
        premiumPkg.style.border = '2px solid #ddd';
        premiumPkg.style.background = 'white';
    } else {
        basicPkg.style.border = '2px solid #ddd';
        basicPkg.style.background = 'white';
        premiumPkg.style.border = '2px solid #C41E3A';
        premiumPkg.style.background = 'rgba(196,30,58,0.1)';
    }
    
    updateTotalValue();
}

// Add to Cart (TODO: implement cart logic)
function addFlowerBouquetToCart() {
    alert('Tính năng đang phát triển: Thêm bó hoa tùy chỉnh vào giỏ hàng');
    // TODO: Send custom bouquet to cart API
    // closeFlowerDetectionModal();
}

// Upload Image or Video from File
function uploadFlowerImage() {
    const input = document.createElement('input');
    input.type = 'file';
    input.accept = 'image/*,video/*';
    
    input.onchange = async (e) => {
        const file = e.target.files[0];
        if (!file) return;
        
        // Validate size
        const maxSize = file.type.startsWith('video/') ? 50 * 1024 * 1024 : 5 * 1024 * 1024;
        if (file.size > maxSize) {
            alert(`Kích thước ${file.type.startsWith('video/') ? 'video' : 'ảnh'} tối đa ${maxSize / (1024 * 1024)}MB`);
            return;
        }
        
        const video = document.getElementById('flowerVideo');
        const uploadedVideo = document.getElementById('flowerUploadedVideo');
        const capturedImage = document.getElementById('flowerCapturedImage');
        const actionsDiv = document.getElementById('flowerActions');
        const captureBtn = document.getElementById('flowerVideoCaptureBtn');
        const liveIndicator = document.getElementById('flowerLiveIndicator');
        
        // Hide all displays first
        video.style.display = 'none';
        uploadedVideo.style.display = 'none';
        capturedImage.style.display = 'none';
        captureBtn.style.display = 'none';
        
        if (file.type.startsWith('video/')) {
            // Handle video upload
            const reader = new FileReader();
            reader.onload = (e) => {
                uploadedVideo.src = e.target.result;
                uploadedVideo.style.display = 'block';
                captureBtn.style.display = 'block';
                
                // Hide action buttons
                if (actionsDiv) actionsDiv.style.display = 'none';
                
                // Start real-time detection when video plays
                uploadedVideo.onplay = () => {
                    if (liveIndicator) liveIndicator.style.display = 'flex';
                    startVideoDetection();
                };
                
                uploadedVideo.onpause = () => {
                    if (liveIndicator) liveIndicator.style.display = 'none';
                    stopVideoDetection();
                };
                
                uploadedVideo.onended = () => {
                    if (liveIndicator) liveIndicator.style.display = 'none';
                    stopVideoDetection();
                };
            };
            reader.readAsDataURL(file);
        } else {
            // Handle image upload
            const reader = new FileReader();
            reader.onload = (e) => {
                capturedImage.src = e.target.result;
                capturedImage.style.display = 'block';
            };
            reader.readAsDataURL(file);
            
            // Process image
            await processFlowerImage(file, true);
        }
    };
    
    input.click();
}

// Start detection from uploaded video
let videoDetectionTimer = null;
function startVideoDetection() {
    if (videoDetectionTimer) return;
    
    videoDetectionTimer = setInterval(async () => {
        if (isFlowerDetectionProcessing) return;
        
        const uploadedVideo = document.getElementById('flowerUploadedVideo');
        if (uploadedVideo.paused || uploadedVideo.ended) return;
        
        isFlowerDetectionProcessing = true;
        
        try {
            const canvas = document.createElement('canvas');
            canvas.width = uploadedVideo.videoWidth;
            canvas.height = uploadedVideo.videoHeight;
            const ctx = canvas.getContext('2d');
            ctx.drawImage(uploadedVideo, 0, 0);
            
            const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', 0.85));
            await processFlowerImage(blob, false); // Show overlay mode
        } catch (error) {
            console.error('Error detecting from video:', error);
        } finally {
            isFlowerDetectionProcessing = false;
        }
    }, 1000);
}

function stopVideoDetection() {
    if (videoDetectionTimer) {
        clearInterval(videoDetectionTimer);
        videoDetectionTimer = null;
    }
    document.getElementById('flowerDetectionResult').innerHTML = '';
    detectionResult = null;
}

// Capture frame from uploaded video
async function captureFromUploadedVideo() {
    if (isFlowerDetectionProcessing) return;
    
    const uploadedVideo = document.getElementById('flowerUploadedVideo');
    const capturedImage = document.getElementById('flowerCapturedImage');
    const captureBtn = document.getElementById('flowerVideoCaptureBtn');
    
    // Pause video and stop detection
    uploadedVideo.pause();
    stopVideoDetection();
    
    isFlowerDetectionProcessing = true;
    
    try {
        const canvas = document.createElement('canvas');
        canvas.width = uploadedVideo.videoWidth;
        canvas.height = uploadedVideo.videoHeight;
        const ctx = canvas.getContext('2d');
        ctx.drawImage(uploadedVideo, 0, 0);
        
        // Show captured frame
        capturedImage.src = canvas.toDataURL('image/jpeg');
        capturedImage.style.display = 'block';
        uploadedVideo.style.display = 'none';
        captureBtn.style.display = 'none';
        
        // Convert to blob and process
        const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', 0.85));
        await processFlowerImage(blob, true); // Show detailed mode
    } catch (error) {
        console.error('Error capturing from video:', error);
        alert('Lỗi khi chụp khung hình. Vui lòng thử lại.');
    } finally {
        isFlowerDetectionProcessing = false;
    }
}
