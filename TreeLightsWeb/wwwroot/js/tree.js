import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';

let camera, scene, renderer, controls, stats;

let mesh;
const amount = parseInt(window.location.search.slice(1)) || 10;
const count = Math.pow(amount, 3);

const raycaster = new THREE.Raycaster();
const mouse = new THREE.Vector2(1, 1);


init();

const connection = new signalR.HubConnectionBuilder()
	.withUrl("/treehub")
	.configureLogging(signalR.LogLevel.Information)
	.build();

connection.on("UpdateLEDs", (ledUpdates) => {
	for (let i = 0; i < ledUpdates.length; i++) {
		mesh.setColorAt(ledUpdates[i].ledIndex, new THREE.Color(ledUpdates[i].newColour.red, ledUpdates[i].newColour.green, ledUpdates[i].newColour.blue));
	};
	mesh.instanceColor.needsUpdate = true;
});

var Start = async () => {
	try {
		await connection.start();
		console.log("SignalR Connected.");
	} catch (err) {
		console.log(err);
		setTimeout(Start, 5000);
	}
};

// Start the connection.
Start();

connection.onclose(async () => {
	await Start();
});

function init() {
	$.get('/Home/LEDCoordinates', function (data) {

		camera = new THREE.PerspectiveCamera(60, window.innerWidth / window.innerHeight, 0.1, 100);
		camera.position.set(0, -5, 0);
		camera.up.set(0, 0, 1);
		camera.lookAt(0, 0, 0);

		scene = new THREE.Scene();

		const light = new THREE.HemisphereLight(0xffffff, 0x888888, 30);
		light.position.set(0, 2, 0);
		scene.add(light);
		scene.background = new THREE.Color(0x666666);

		const geometry = new THREE.IcosahedronGeometry(0.05, 3);
		const material = new THREE.MeshPhongMaterial({ color: 0xffffff });


		mesh = new THREE.InstancedMesh(geometry, material, data.length);

		let i = 0;

		const matrix = new THREE.Matrix4();
		var black = new THREE.Color(0, 0, 0);

		for (let i = 0; i < data.length; i++) {
			matrix.setPosition(parseFloat(data[i].x), parseFloat(data[i].y), parseFloat(data[i].z));
			//matrix.setPosition(parseFloat(data[i].originalx), parseFloat(data[i].originaly), parseFloat(data[i].originalz));
			mesh.setMatrixAt(i, matrix);
			mesh.setColorAt(i, black);
		}

		scene.add(mesh);

		renderer = new THREE.WebGLRenderer({ antialias: true });
		renderer.setPixelRatio(window.devicePixelRatio);
		renderer.setSize(window.innerWidth, window.innerHeight);
		renderer.setAnimationLoop(animate);
		document.body.appendChild(renderer.domElement);

		controls = new OrbitControls(camera, renderer.domElement);
		controls.enableDamping = true;
		controls.enableZoom = true;
		controls.enablePan = false;

		window.addEventListener('resize', onWindowResize);
		document.addEventListener('mousemove', onMouseMove);
	});
}

function onWindowResize() {

	camera.aspect = window.innerWidth / window.innerHeight;
	camera.updateProjectionMatrix();

	renderer.setSize(window.innerWidth, window.innerHeight);

}

function onMouseMove(event) {

	event.preventDefault();

	mouse.x = (event.clientX / window.innerWidth) * 2 - 1;
	mouse.y = - (event.clientY / window.innerHeight) * 2 + 1;

}

function animate() {

	controls.update();

	raycaster.setFromCamera(mouse, camera);

	const intersection = raycaster.intersectObject(mesh);

	if (intersection.length > 0) {

		const instanceId = intersection[0].instanceId;
		// We have the led id under the mouse pointer here
	}

	renderer.render(scene, camera);

}