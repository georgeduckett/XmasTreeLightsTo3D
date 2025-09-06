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
		var selectedLedIndex = 0;
		
		var container = document.getElementById('treediv');

		camera = new THREE.PerspectiveCamera(60, container.clientWidth / container.clientHeight, 0.1, 2000);

		scene = new THREE.Scene();

		const light = new THREE.HemisphereLight(0xffffff, 0x888888, 30);
		light.position.set(0, 2, 0);
		scene.add(light);
		scene.background = new THREE.Color(0x666666);

		scene.add(new THREE.GridHelper(1000, 1000));

		const geometry = new THREE.IcosahedronGeometry(0.05, 3);
		const material = new THREE.MeshPhongMaterial({ color: 0xffffff });


		const lineMaterial = new THREE.LineBasicMaterial({ color: 0x00aa00 });


		mesh = new THREE.InstancedMesh(geometry, material, data.length);

		let i = 0;

		const matrix = new THREE.Matrix4();
		var black = new THREE.Color(0, 0, 0);
		var red = new THREE.Color(255, 0, 0);
		var green = new THREE.Color(0, 255, 0);

		const linePoints = [];

		for (let i = 0; i < data.length; i++) {
			linePoints.push(new THREE.Vector3(parseFloat(data[i].x), parseFloat(data[i].z), parseFloat(data[i].y)));
			matrix.setPosition(parseFloat(data[i].x), parseFloat(data[i].z), parseFloat(data[i].y));

			mesh.setMatrixAt(i, matrix);
			mesh.setColorAt(i, data[i].wascorrected === "True" ? red : black);
		}

		const lineGeometry = new THREE.BufferGeometry().setFromPoints(linePoints);
		const line = new THREE.Line(lineGeometry, lineMaterial);
		line.visible = false;
		scene.add(line);
		scene.add(mesh);

		renderer = new THREE.WebGLRenderer({ antialias: true });
		renderer.setPixelRatio(container.clientWidth / container.clientHeight);
		renderer.setSize(container.clientWidth, container.clientHeight);
		renderer.setAnimationLoop(animate);



		container.appendChild(renderer.domElement);

		var maxZ = data.map(x => x.z).reduce((a, b) => Math.max(a, b));
		var maxZOriginal = data.map(x => x.originalz).reduce((a, b) => Math.max(a, b));

		controls = new OrbitControls(camera, renderer.domElement);
		controls.enableDamping = true;
		controls.enableZoom = true;
		controls.enablePan = false;
		camera.position.set(0, maxZ / 2, -5);
		controls.target.set(0, maxZ / 2, 0);
		controls.update();

		container.addEventListener('resize', onWindowResize);
		container.addEventListener('mousedown', onMouseDown);


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
				camera.position.set(0, maxZOriginal / 2, -500);
				controls.update();
			}
			else {
				matrix.scale(new THREE.Vector3(0.01, 0.01, 0.01));
				camera.position.set(0, maxZ / 2, -5);
				controls.update();

			}
			for (let i = 0; i < data.length; i++) {
				if (value) {
					linePoints[i] = new THREE.Vector3(parseFloat(data[i].originalx), parseFloat(data[i].originalz), parseFloat(data[i].originaly));
					matrix.setPosition(parseFloat(data[i].originalx), parseFloat(data[i].originalz), parseFloat(data[i].originaly));
					
				}
				else {
					linePoints[i] = new THREE.Vector3(parseFloat(data[i].x), parseFloat(data[i].z), parseFloat(data[i].y));
					matrix.setPosition(parseFloat(data[i].x), parseFloat(data[i].z), parseFloat(data[i].y));
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

		function onWindowResize() {

			camera.aspect = container.clientWidth / container.clientHeight;
			camera.updateProjectionMatrix();

			renderer.setSize(container.clientWidt, container.clientHeight);
		}

		function onMouseDown(e) {
			e.preventDefault();
			var flags = e.buttons !== undefined ? e.buttons : e.which;
			if ((flags & 1) === 1) {
				// Left mouse button pressed
				var rect = container.getBoundingClientRect();
				mouse.x = ((e.clientX - rect.left) / rect.width) * 2 - 1;
				mouse.y = - ((e.clientY - rect.top) / rect.height) * 2 + 1;

				raycaster.setFromCamera(mouse, camera);

				const intersection = raycaster.intersectObject(mesh);

				if (intersection.length > 0) {

					const instanceId = intersection[0].instanceId;
					if (selectedLedIndex !== instanceId) {
						$('#ledNumber').val(instanceId);
						changeLed();
					}
				}
			}
		}

		function animate() {

			controls.update();

			renderer.render(scene, camera);

		}

		function changeLed() {
			// Reset the colour of the old index
			mesh.setColorAt(parseInt(selectedLedIndex), data[selectedLedIndex].wascorrected === "True" ? red : black);

			selectedLedIndex = $('#ledNumber').val();

			// Set the colour of the new index
			mesh.setColorAt(parseInt(selectedLedIndex), green);
			mesh.instanceColor.needsUpdate = true;

			for (let i = 0; i < 8; i++) {
				let canvas = document.getElementById('canvas' + i);
				let ctx = canvas.getContext('2d');
				let img = new Image();
				img.onload = function () {
					canvas.width = img.width;
					canvas.height = img.height;
					ctx.drawImage(img, 0, 0);
					
					// Having drawn the image, now update it with the found coordinates, if there are any
					$.ajax({
						url: `/ImageProcessing/GetStoredImageData/?ledIndex=${selectedLedIndex}&treeAngleIndex=${i}`,
						success: function (data) {
							if (data != null) {
								var x = data.MaxLoc.X;
								var y = data.MaxLoc.Y;
								ctx.beginPath();
								ctx.moveTo(x, 0);
								ctx.lineTo(x, y - 5);
								ctx.moveTo(x, y + 5);
								ctx.lineTo(x, canvas.height);
								ctx.moveTo(0, y);
								ctx.lineTo(x - 5, y);
								ctx.moveTo(x + 5, y);
								ctx.lineTo(canvas.width, y);
								ctx.strokeStyle = 'green';
								ctx.lineWidth = 2;
								ctx.stroke();
							}
						},
						error: function (error) {
							// TODO: If 404 error ignore it
							alert(error.responseText);
						}
					});
				};
				img.src = '/CapturedImages/' + selectedLedIndex + '_' + (i * 45) + '.png';
			}
		}

		changeLed();

		$('#changeled').click(changeLed);


	});
}