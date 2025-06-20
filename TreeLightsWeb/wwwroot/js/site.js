$.get('/Home/IsTreeConnected', function (data) {
    if (data === "False") {
        $('.treenotconnectednav').show();
    }
});