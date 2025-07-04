function checkTreeConnection() {
    $.get('/Home/IsTreeConnected', function (data) {
        if (data === "False") {
            $('.treenotconnectednav').show();
            setTimeout(checkTreeConnection, 5000);
        } else {
            $('.treenotconnectednav').hide();
        }
    });
}

window.addEventListener('DOMContentLoaded', function () {
    checkTreeConnection();
});