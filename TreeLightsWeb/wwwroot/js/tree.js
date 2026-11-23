import * as THREE from 'three';
import { GUI } from 'three/addons/libs/lil-gui.module.min.js';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';

let camera, scene, renderer, controls, stats;

let mesh;
const amount = parseInt(window.location.search.slice(1)) || 10;
const count = Math.pow(amount, 3);

const raycaster = new THREE.Raycaster();
const mouse = new THREE.Vector2(1, 1);
const ledColour = new THREE.Color();


init();

const connection = new signalR.HubConnectionBuilder()
	.withUrl("/treehub")
	.configureLogging(signalR.LogLevel.Information)
	.build();

connection.on("UpdateLEDs", (ledUpdates) => {
	if (!mesh) return; // (the tree's still loading)
	for (let i = 0; i < ledUpdates.length; i++) {
		// The colours are 0-255 sRGB values (what's sent to WLED)
		ledColour.setRGB(ledUpdates[i].newColour.red / 255, ledUpdates[i].newColour.green / 255, ledUpdates[i].newColour.blue / 255, THREE.SRGBColorSpace);
		mesh.setColorAt(ledUpdates[i].ledIndex, ledColour);
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
	// Show the tree from the side that faces the room (set on the Configuration page), the right way round
	$.getJSON('/Configuration/Settings')
		.done(function (config) { loadTree(config.settings.frontDirection || 0, !!config.settings.mirrored); })
		.fail(function () { loadTree(0, false); });
}

function loadTree(frontDirection, mirrored) {
	// The viewer's direction from the trunk (0 is where the capture camera stood, at -y) and their right
	const viewAngle = (frontDirection - 90) * Math.PI / 180;
	const rightX = -Math.sin(viewAngle), rightY = Math.cos(viewAngle);
	// The tree's z is up, and three.js's y is, so its (x, y, z) is (x, z, -y) (just swapping y and z would mirror it).
	// If the positions are a mirror image of the tree, flip them left to right as seen from the front.
	function toScene(x, y, z) {
		if (mirrored) {
			const across = x * rightX + y * rightY;
			x -= 2 * across * rightX;
			y -= 2 * across * rightY;
		}
		return new THREE.Vector3(x, z, -y);
	}

	$.ajax({
		url: '/Home/LEDCoordinates',
		type: 'GET',
		success: function (data) {
			camera = new THREE.PerspectiveCamera(60, window.innerWidth / window.innerHeight, 0.1, 2000);

			scene = new THREE.Scene();

			const light = new THREE.HemisphereLight(0xffffff, 0x888888, 30);
			light.position.set(0, 2, 0);
			scene.add(light);
			scene.background = new THREE.Color(0x666666);

			scene.add(new THREE.GridHelper(1000, 1000));

			const geometry = new THREE.IcosahedronGeometry(0.05, 3);
			const material = new THREE.MeshBasicMaterial({ color: 0xffffff });


			const lineMaterial = new THREE.LineBasicMaterial({ color: 0x00aa00 });


			mesh = new THREE.InstancedMesh(geometry, material, data.length);

			let i = 0;

			const matrix = new THREE.Matrix4();
			var black = new THREE.Color(0, 0, 0);
			var red = new THREE.Color(255, 0, 0);

			const linePoints = [];

			for (let i = 0; i < data.length; i++) {
				const position = toScene(parseFloat(data[i].x), parseFloat(data[i].y), parseFloat(data[i].z));
				linePoints.push(position);
				matrix.setPosition(position);

				mesh.setMatrixAt(i, matrix);
				mesh.setColorAt(i, black);
			}

			const lineGeometry = new THREE.BufferGeometry().setFromPoints(linePoints);
			const line = new THREE.Line(lineGeometry, lineMaterial);
			line.visible = false;
			scene.add(line);
			scene.add(mesh);

			renderer = new THREE.WebGLRenderer({ antialias: true });
			renderer.setPixelRatio(window.devicePixelRatio);
			renderer.setSize(window.innerWidth, window.innerHeight);
			renderer.setAnimationLoop(animate);
			document.body.appendChild(renderer.domElement);

			var maxZ = data.map(x => x.z).reduce((a, b) => Math.max(a, b));
			var maxZOriginal = data.map(x => x.originalz).reduce((a, b) => Math.max(a, b));

			controls = new OrbitControls(camera, renderer.domElement);
			controls.enableDamping = true;
			controls.enableZoom = true;
			controls.enablePan = false;
			camera.position.set(5 * Math.cos(viewAngle), maxZ / 2, -5 * Math.sin(viewAngle)); // in front of the tree
			controls.target.set(0, maxZ / 2, 0);
			controls.update();

			window.addEventListener('resize', onWindowResize);
			document.addEventListener('mousemove', onMouseMove);



			const uiState = {
				showOriginalPositions: false,
				showWire: false,
			};

			const gui = new GUI();
			// TODO: Use sourcecode from https://hofk.de/main/discourse.threejs/2017/PictureBall/PictureBall.html to create a billboard per sphere
			// to show the index that updates it's position to always face the camera and be just in front of the sphere

			gui.add(uiState, 'showOriginalPositions').onChange((value) => {
				if (value) {
					matrix.scale(new THREE.Vector3(100, 100, 100));
					camera.position.set(0, -500, 0);
				}
				else {
					matrix.scale(new THREE.Vector3(0.01, 0.01, 0.01));
					camera.position.set(0, -5, 0);

				}
				for (let i = 0; i < data.length; i++) {
					if (value) {
						linePoints[i] = toScene(parseFloat(data[i].originalx), parseFloat(data[i].originaly), parseFloat(data[i].originalz));
						matrix.setPosition(linePoints[i]);

					}
					else {
						linePoints[i] = toScene(parseFloat(data[i].x), parseFloat(data[i].y), parseFloat(data[i].z));
						matrix.setPosition(linePoints[i]);
						matrix.scale(new THREE.Vector3(1, 1, 1));
					}
					mesh.setMatrixAt(i, matrix);
					mesh.instanceMatrix.needsUpdate = true;

				}

				lineGeometry.setFromPoints(linePoints);
			});

			gui.add(uiState, 'showWire').onChange((value) => {
				line.visible = value;
			});
		},
		error: function (err) {
			alert(err.responseText + "\n\nError loading LED coordinates from file. Have you captured and processed the tree images?");
		}
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