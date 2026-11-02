'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const { makeBeam, clipBeamToRect, polygonArea } = require('../../GaoApp.Web/wwwroot/Admin/js/account-login.js');

test('a flashlight facing away leaves the password and owl outside the light', () => {
    const beam = makeBeam({ x: 400, y: 200 }, { x: 500, y: 300 });
    for (const rect of [{ x: 40, y: 180, w: 300, h: 40 }, { x: 100, y: 80, w: 60, h: 60 }]) {
        assert.equal(polygonArea(clipBeamToRect(beam.points, rect)), 0);
    }
});

test('light reveals only the intersecting part of the password field', () => {
    const rect = { x: 40, y: 180, w: 300, h: 40 };
    const polygon = clipBeamToRect(makeBeam({ x: 380, y: 200 }, { x: 250, y: 200 }).points, rect);
    const area = polygonArea(polygon);
    assert.ok(area > 0 && area < rect.w * rect.h / 2);
    assert.ok(polygon.every(p => p.x >= rect.x && p.x <= rect.x + rect.w && p.y >= rect.y && p.y <= rect.y + rect.h));
});

test('flashlight can reach decorations above a tall mobile POS pairing form', () => {
    const beam = makeBeam({ x: 270, y: 1500 }, { x: 35, y: 80 });
    assert.ok(beam.length > 1400);
    assert.ok(polygonArea(clipBeamToRect(beam.points, { x: 0, y: 50, w: 70, h: 70 })) > 100);
});

test('clipping handles a corner hit and excludes an empty target', () => {
    const cone = makeBeam({ x: 0, y: 0 }, { x: 100, y: 100 }).points;
    const intersection = clipBeamToRect(cone, { x: 95, y: 75, w: 40, h: 40 });
    assert.ok(polygonArea(intersection) > 0);
    assert.equal(polygonArea(clipBeamToRect(cone, { x: -30, y: -30, w: 10, h: 10 })), 0);
    assert.equal(polygonArea([]), 0);
});

test('minimum flashlight reach covers short password text when aimed at the field center', () => {
    const beam = makeBeam({ x: 1120, y: 559 }, { x: 947, y: 559 }, 414);
    const shortPassword = { x: 760, y: 550, w: 36, h: 18 };
    assert.equal(polygonArea(clipBeamToRect(beam.points, shortPassword)), shortPassword.w * shortPassword.h);
});

test('subpixel mouse rounding at the flashlight button cannot aim it at the owl', () => {
    const beam = makeBeam({ x: 1120.4, y: 559.3 }, { x: 1120, y: 559 }, 414);
    assert.equal(beam.angle, 0);
    assert.equal(polygonArea(clipBeamToRect(beam.points, { x: 805, y: 370, w: 62, h: 62 })), 0);
});
