import * as THREE from 'three';
import { GUI } from 'three/addons/libs/lil-gui.module.min.js';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';

let camera, scene, renderer, controls, stats;

let mesh;
const amount = parseInt(window.location.search.slice(1)) || 10;
const count = Math.pow(amount, 3);

const raycaster = new THREE.Raycaster();
const mouse = new THREE.Vector2(1, 1);

var guiContainer = document.getElementById('guidiv');

init();

function init() {
	$.get('/Home/LEDCoordinates', function (data) {
		camera = new THREE.PerspectiveCamera(60, window.innerWidth / window.innerHeight, 0.1, 2000);
		camera.position.set(0, -5, 0);
		camera.up.set(0, 0, 1);
		camera.lookAt(0, 0, 0);

		scene = new THREE.Scene();

		const light = new THREE.HemisphereLight(0xffffff, 0x888888, 30);
		light.position.set(0, 2, 0);
		scene.add(light);
		scene.background = new THREE.Color(0x666666);
		scene.add(new THREE.AxesHelper(3));

		const geometry = new THREE.IcosahedronGeometry(0.05, 3);
		const material = new THREE.MeshPhongMaterial({ color: 0xffffff });


		const lineMaterial = new THREE.LineBasicMaterial({ color: 0x00aa00 });


		mesh = new THREE.InstancedMesh(geometry, material, data.length);

		let i = 0;

		const matrix = new THREE.Matrix4();
		var black = new THREE.Color(0, 0, 0);
		var red = new THREE.Color(255, 0, 0);

		const linePoints = [];

		// TODO: Shift data's z coords down by half

		for (let i = 0; i < data.length; i++) {
			linePoints.push(new THREE.Vector3(parseFloat(data[i].x), parseFloat(data[i].y), parseFloat(data[i].z)));
			matrix.setPosition(parseFloat(data[i].x), parseFloat(data[i].y), parseFloat(data[i].z));

			mesh.setMatrixAt(i, matrix);
			mesh.setColorAt(i, data[i].wascorrected === "True" ? red : black);
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


		var container = document.getElementById('treediv');

		container.appendChild(renderer.domElement);

		controls = new OrbitControls(camera, renderer.domElement);
		controls.enableDamping = true;
		controls.enableZoom = true;
		controls.enablePan = false;

		container.addEventListener('resize', onWindowResize);
		container.addEventListener('mousemove', onMouseMove);



		const uiState = {
			showOriginalPositions: false,
			showWire: false,
		};

		const gui = new GUI({ autoPlace: false });

		guiContainer.appendChild(gui.domElement);

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
					linePoints[i] = new THREE.Vector3(parseFloat(data[i].originalx), parseFloat(data[i].originaly), parseFloat(data[i].originalz));
					matrix.setPosition(parseFloat(data[i].originalx), parseFloat(data[i].originaly), parseFloat(data[i].originalz));
					
				}
				else {
					linePoints[i] = new THREE.Vector3(parseFloat(data[i].x), parseFloat(data[i].y), parseFloat(data[i].z));
					matrix.setPosition(parseFloat(data[i].x), parseFloat(data[i].y), parseFloat(data[i].z));
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
	});
}

function onWindowResize() {

	camera.aspect = window.innerWidth / window.innerHeight;
	camera.updateProjectionMatrix();

	renderer.setSize(window.innerWidth, window.innerHeight);

}

function onMouseMove(event) {

	event.preventDefault();
	
	mouse.x = (event.clientX / guiContainer.clientWidth) * 2 - 1;
	mouse.y = - (event.clientY / guiContainer.clientHeight) * 2 + 1;

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